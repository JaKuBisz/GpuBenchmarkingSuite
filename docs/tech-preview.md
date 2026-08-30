# Technologický přehled praktické části — GpuBenchmarks

## Cíl aplikace

Konzolová aplikace pro systematické porovnání výkonu GPU a CPU parallelizace ve dvou úlohách (násobení matic, Game of Life). Měří absolutní časy, speedup, škálování a umožňuje porovnat různé GPU programovací frameworky.

---

## Platforma a runtime

| Položka | Hodnota |
|---|---|
| Jazyk | **C# 14** |
| Runtime | **.NET 10** (`net10.0`) |
| Architektura | **x64** only (`<Platforms>x64</Platforms>`) |
| Unsafe bloky | Povoleny (`<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`) |
| Optimalizace | Release mode (`<Optimize>true</Optimize>`) |
| Projekt | `GpuBenchmarks.sln` → jediný projekt `GpuBenchmarks.csproj` |

---

## NuGet závislosti

| Balíček | Verze | Účel |
|---|---|---|
| **ILGPU** | 1.5.3 | GPU kernel spouštění (Cuda + OpenCL + CPU emulátor) |
| **ILGPU.Algorithms** | 1.5.3 | Math utility funkce nad ILGPU |
| **ComputeSharp** | 3.1.0 | DirectX 12 / HLSL compute shadery |
| **System.Management** | 10.0.5 | WMI dotazy pro system info (Windows only) |

---

## Architektura kódu

### Rozhraní `IBenchmark`

Každá varianta implementuje jedno rozhraní se 5 metodami:

```csharp
interface IBenchmark {
    void Setup(int size);                       // alokace dat, inicializace GPU kontextu
    void Run();                                 // samotný výpočet (měří se jen toto)
    bool Validate(IBenchmark reference);        // přesnost vůči sequential
    void Cleanup();                             // uvolnění zdrojů
    float[] GetResult();                        // pro validaci
}
```

### `BenchmarkRunner`

Řídí životní cyklus jednoho benchmarku:

1. **Warmup** (default 3 běhy, `--quick` → 1 běh) — Setup+Run+Cleanup
2. **Probe run** — jeden měřený běh pro odhad délky
3. **Adaptive reduction** — při `estimatedMs > 60 000` snižuje počet běhů na 3, při `> 30 000` na 5
4. **Měřené běhy** (default 10 běhů, `--runs N` pro override)
5. **Validace** na prvním měřeném běhu oproti sekvenční referenci
6. Vrací `List<BenchmarkResult>` → statistiky přes `ComputeStats()` (medián, min, max, stddev)

### `GpuSelector`

Spravuje výběr GPU zařízení:

- Enumeruje všechny ILGPU akcelerátory přes `Context.CreateDefault()`
- Priorita výběru: `--gpu <index>` CLI arg → env var `GPUBENCH_GPU` → automaticky pokud jedno GPU → interaktivní prompt
- `CreateAccelerator(int index)` → vrací `(Context, Accelerator)` tuple, volající zodpovídá za Dispose

### `Program.cs` — orchestrace

Top-level statements (C# 9+), řídí celý flow. Klíčová rozhodnutí:

- Variantu ILGPU pojmenuje dynamicky `ILGPU_Cuda` / `ILGPU_OpenCL` podle skutečného backendu zvoleného zařízení
- `raw_results.csv` se **průběžně proplachuje** po každé velikosti (`File.WriteAllText`)
- `summary.csv` se zapíše **jediný jednou na konci** programu — toto je designový nedostatek

---

## GPU Framework 1 — ILGPU

**ILGPU** (verze 1.5.3, open-source, MIT licence) je C# wrapper umožňující psát GPU kernely jako **čisté C# metody** s automatickým překladem do platformově specifického kódu.

### Podporované backendy (na testovacím stroji)

```
[0] CPUAccelerator  — CPU emulátor (fallback, 16 threads/group)
[1] Intel Iris Xe   — OpenCL backend (512 threads/group, 30 GB shared memory)
[2] NVIDIA T600     — CUDA backend  (1024 threads/group, 4 GB VRAM)
```

### Jak funguje překlad kernelů

Kernel je statická C# metoda označená typem `Index1D` jako první parametr. ILGPU ji při `LoadAutoGroupedStreamKernel<>()` zkompiluje:

- Pro CUDA backend → **PTX** (Parallel Thread Execution)
- Pro OpenCL backend → **OpenCL IL / SPIR-V**
- Pro CPU backend → nativní x64 strojový kód

### Způsob použití v projektu

**MatrixMultiply kernel** (naivní, bez tilingu):

```csharp
static void MatMulKernel(Index1D index,
    ArrayView1D<float, Stride1D.Dense> a,
    ArrayView1D<float, Stride1D.Dense> b,
    ArrayView1D<float, Stride1D.Dense> c, int n)
{
    int row = index / n;
    int col = index % n;
    float sum = 0f;
    for (int k = 0; k < n; k++)
        sum += a[row * n + k] * b[k * n + col];
    c[row * n + col] = sum;
}
```

Spouštěn přes `_kernel!(n*n, bufA.View, bufB.View, bufC.View, n)` — `n*n` vláken, každé počítá jeden prvek.

**Game of Life kernel** (stencil pattern, toroidální hranice):

```csharp
static void GoLKernel(Index1D index,
    ArrayView1D<int, Stride1D.Dense> current,
    ArrayView1D<int, Stride1D.Dense> next, int n)
{
    int x = index / n; int y = index % n;
    // počítá 8 sousedů s wrap-around: (x + dx + n) % n
    // aplikuje Conway pravidla → next[x*n+y]
}
```

- **Ping-pong double buffering**: 100 generací, každá volá kernel s prohozenými buffery A↔B
- `acc.Synchronize()` po každém kroku pro správný výsledek

### Správa paměti (ILGPU)

- `acc.Allocate1D<T>(length)` → GPU buffer
- `buf.CopyFromCPU(array)` / `buf.CopyToCPU(array)` — explicitní host↔device přenosy
- Buffery jsou `using` — automatické uvolnění

---

## GPU Framework 2 — ComputeSharp

**ComputeSharp** (verze 3.1.0, open-source) generuje **HLSL compute shadery** pro **DirectX 12**. Funguje výhradně na Windows s DX12-kompatibilní GPU.

### Mechanismus source generátoru

Shadery jsou C# `readonly partial struct` implementující `IComputeShader`. Atributy `[GeneratedComputeShaderDescriptor]` a `[ThreadGroupSize(...)]` aktivují **Roslyn source generator**, který generuje HLSL kód v době kompilace.

**MatrixMultiply shader:**

```csharp
[ThreadGroupSize(DefaultThreadGroupSizes.X)]  // 64 vláken/group
[GeneratedComputeShaderDescriptor]
internal readonly partial struct MatMulShader : IComputeShader
{
    public readonly ReadOnlyBuffer<float> A, B;
    public readonly ReadWriteBuffer<float> C;
    public readonly int N;

    public void Execute()
    {
        int index = ThreadIds.X;  // HLSL: SV_DispatchThreadID
        int row = index / N;
        int col = index % N;
        float sum = 0f;
        for (int k = 0; k < N; k++)
            sum += A[row * N + k] * B[k * N + col];
        C[row * N + col] = sum;
    }
}
```

**Game of Life shader:** stejný ping-pong pattern jako ILGPU, 100 generací s `device.For(total, new GoLShader(bufA, bufB, n))`.

### Správa paměti (ComputeSharp)

- `device.AllocateReadOnlyBuffer<T>(array)` — upload + read-only GPU buffer
- `device.AllocateReadWriteBuffer<T>(size)` — read-write GPU buffer
- `bufC.CopyTo(destArray)` — download
- Všechny buffery přes `using` (IDisposable)

---

## CPU varianty

### Sequential

Čisté trojité `for` smyčky bez paralelismu. Referenční baseline pro speedup výpočty.

### Parallel (TPL)

Využívá **Task Parallel Library** ze standardní .NET knihovny:

```csharp
Parallel.For(0, n, new ParallelOptions { MaxDegreeOfParallelism = _threadCount }, i => { ... });
```

- Testované počty vláken: **1, 2, 4, 8, max** (kde `max = Environment.ProcessorCount = 20`)
- Paralelizace na úrovni **řádků** (každé vlákno zpracuje jeden nebo více řádků)
- Pro GoL: paralelizace vnějšího cyklu přes `y` (řádky mřížky), vnitřní smyčky sekvenční

---

## Výstupní formát

Čtyři výstupní soubory v adresáři `results/`:

| Soubor | Obsah | Kdy se zapisuje |
|---|---|---|
| `raw_results.csv` | Každý jednotlivý běh: TaskName, Variant, InputSize, RunNumber, TimeMs, IsValid | Průběžně po každé velikosti |
| `summary.csv` | Agregace: MedianMs, MinMs, MaxMs, StdDevMs, SpeedupVsSequential, IsValid | Jednou na konci programu |
| `amdahl_analysis.csv` | Per-velikost: MeasuredSpeedupActual, EstimatedP (podíl par. kódu), TheoreticalSpeedup_8cores | Jednou na konci |
| `system_info.txt` | CPU, GPU (WMI), ILGPU zařízení, .NET verze, datum | Na začátku |

**Speedup** = `seqMedian / variantMedian` (medián ze stejné velikosti)

**Amdahlovo P** odvozeno z naměřeného zrychlení S při N vláknech:

$$P = \frac{\frac{1}{S} - 1}{\frac{1}{N} - 1}$$

---

## CLI parametry

| Parametr | Popis |
|---|---|
| `--gpu <index>` | Vynutí konkrétní ILGPU zařízení (index z `--list-devices`) |
| `--list-devices` | Vypíše dostupná ILGPU zařízení a ukončí se |
| `--quick` | Malé velikosti (64/128/256; GoL 100/500), 1 warmup, 3 běhy, jen 1+max vláken |
| `--runs N` | Override počtu měřených běhů |
| `--sizes A,B,C` | Vlastní seznam velikostí matic |
| `--gol-sizes A,B,C` | Vlastní seznam velikostí GoL mřížky |
| `GPUBENCH_GPU=N` | Env var alternativa k `--gpu` |

---

## Testovací hardware (skutečná konfigurace)

| Komponenta | Specifikace |
|---|---|
| CPU | Intel Core i7-12800H (12. gen Alder Lake), 20 logických vláken (6P+8E cores) |
| GPU | **NVIDIA T600 Laptop GPU**, 4 GB GDDR6 VRAM, CUDA backend |
| GPU sekundární | Intel Iris Xe Graphics, 2 GB shared VRAM, OpenCL backend (neměřeno) |
| OS | Windows 10 build 26100, 64-bit |
| .NET | 10.0 |
| ILGPU | 1.5.3 |

> Ve všech naměřených datech byl aktivní **CUDA backend** (`--gpu 2`, NVIDIA T600). OpenCL varianta (Intel Iris Xe) nebyla benchmarkována.

---

## Struktura souborů

```
GpuBenchmarks/
├── Program.cs                        # Orchestrace, CLI parsing, CSV výstup
├── BenchmarkRunner.cs                # Warmup/probe/measure logika, statistiky
├── BenchmarkResult.cs                # POCO datová třída pro jeden výsledek
├── IBenchmark.cs                     # Rozhraní pro všechny varianty
├── GpuSelector.cs                    # ILGPU device discovery a selection
├── MatrixMultiply/
│   ├── MatrixMultiplySequential.cs   # 3× for cyklus, float32
│   ├── MatrixMultiplyParallel.cs     # TPL Parallel.For přes řádky
│   ├── MatrixMultiplyGpu.cs          # ILGPU kernel (naivní, 1 vlákno/prvek)
│   └── MatrixMultiplyComputeSharp.cs # ComputeSharp/DX12 shader
└── GameOfLife/
    ├── GameOfLifeSequential.cs       # 100 kroků, 3× for, swap bufferů
    ├── GameOfLifeParallel.cs         # 100 kroků, TPL přes řádky y
    ├── GameOfLifeGpu.cs              # ILGPU kernel + ping-pong GPU buffery
    └── GameOfLifeComputeSharp.cs     # ComputeSharp shader + ping-pong
```

---

## Klíčové designové rozhodnutí a omezení

1. **Kernely jsou naivní** — bez sdílené paměti (tiling), bez optimalizace přístupu do paměti. Záměr: srovnat přístup "přímočará GPU implementace" vs CPU, ne optimalizovanou CUDA produkční verzi.
2. **Validace** probíhá porovnáním pouze prvních 100 prvků vůči sekvenčnímu výsledku s tolerancí `1e-2f` (float32 nepřesnost).
3. **Summary.csv vs raw_results.csv** — summary se zapisuje jen jednou na konci; při přerušení běhu jsou zachráněna data pouze v raw CSV.
4. **OpenCL nebyl naměřen** — framework to umožňuje (`--gpu 1`), ale ve výsledcích je pouze CUDA.
5. **ComputeSharp chybí pro 2048² MatrixMultiply** — pravděpodobně OOM nebo timeout při DX12 alokaci.
