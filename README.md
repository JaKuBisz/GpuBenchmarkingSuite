# GPU Benchmark Suite

[![.NET](https://github.com/JaKuBisz/gpu-vs-cpu-benchmarks/actions/workflows/dotnet.yml/badge.svg)](https://github.com/JaKuBisz/gpu-vs-cpu-benchmarks/actions/workflows/dotnet.yml)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![ILGPU](https://img.shields.io/badge/ILGPU-1.5.3-orange)](https://ilgpu.net/)
[![ComputeSharp](https://img.shields.io/badge/ComputeSharp-3.1.0-blue)](https://github.com/Sergio0694/ComputeSharp)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
![Platform](https://img.shields.io/badge/platform-Windows%20x64-lightgrey)

**When is a GPU actually worth it?** This suite answers that empirically for two classic
workloads, comparing sequential CPU, parallel CPU and two GPU frameworks on the same
algorithm, with correctness validation, statistical rigour and reproducible CSV output.

Built in C# / .NET 10 as the practical component of a master's thesis on
**GPU computation parallelization** at the Silesian University in Opava.

---

## Headline findings

> The interesting result is that "use the GPU" is not universally good advice. The right
> choice depends entirely on the ratio of arithmetic work to memory transfer, and the
> crossover between the two rows below is the whole story.

| Workload | Size | Sequential CPU | Best GPU variant | Verdict |
|---|---|---|---|---|
| Matrix multiply | 64×64 | **0.19 ms** | 2.20 ms (ComputeSharp) | GPU **11× slower**. Transfer and launch overhead dominate |
| Matrix multiply | 4096×4096 | 1463 s | **224 ms** (ComputeSharp) | GPU **6500× faster**. Arithmetic finally outweighs transfer |
| Game of Life | 1000×1000, 100 steps | 5750 ms | **8.4 ms** (ILGPU/CUDA) | GPU **688× faster**. 100 kernel iterations amortise one upload |

Two takeaways that generalise:

1. **Parallel is not automatically faster.** `Parallel.For` with one thread was slower
   than a plain loop (0.33×), because the partitioner and delegate dispatch are pure
   overhead until the work per iteration is large enough.
2. **The two GPU frameworks are not interchangeable.** ILGPU beats ComputeSharp by an
   order of magnitude on some workloads and loses on others, which is exactly why the
   suite benchmarks both.

<sub>Median of measured runs. Full data in [`GpuBenchmarks/results/`](GpuBenchmarks/results/),
including weak-scaling (Gustafson) and ILGPU granularity sweeps. Run the suite on your
own hardware to reproduce.</sub>

---

## Why this project is interesting

- **Honest GPU timing.** Every GPU measurement includes buffer allocation, host→device
  copy, kernel execution, synchronisation and device→host copy back. No cherry-picking
  kernel-only numbers.
- **Correctness is enforced, not assumed.** Every variant is validated against the
  sequential reference before its timing is accepted, and a
  [test suite](GpuBenchmarks.Tests/) covers the statistics, parsing and scaling math.
- **Adaptive measurement protocol.** Warmup runs, a probe run to size the workload, then
  10/5/3 measured runs depending on cost, reported as median, min, max, stddev and CV.
- **Three scaling stories.** Strong scaling with Amdahl analysis, weak scaling with
  strict Gustafson measurement, and a GPU-vs-GPU parallel granularity sweep.
- **Reproducible.** Fixed RNG seed, pinned SDK, CI on every push.

---

## Quick Start

Windows 10/11 x64 with the .NET 10 SDK:

```powershell
# From the repo root: checks dependencies, restores packages, builds
.\setup.ps1

# Quick smoke run (finishes in seconds)
dotnet run -c Release --project GpuBenchmarks -- --quick

# Full strong-scaling run
dotnet run -c Release --project GpuBenchmarks
```

Common scenarios:

```powershell
# List GPU devices the runtimes can see
dotnet run -c Release --project GpuBenchmarks -- --list-devices

# Limit variants and sizes
dotnet run -c Release --project GpuBenchmarks -- --quick --variants seq,parallel_4,ilgpu,computesharp --sizes 64,128 --gol-sizes 8,16

# Weak scaling (Gustafson), with strict same-workload sequential baseline
dotnet run -c Release --project GpuBenchmarks -- --weak-scale --weak-base 512 --weak-gustafson-strict

# ILGPU granularity sweep (GPU-vs-GPU by elements-per-thread)
dotnet run -c Release --project GpuBenchmarks -- --ilgpu-scaling --ilgpu-ept 64,32,16,8,4,2,1 --sizes 512 --runs 5

# Regenerate analysis CSVs from saved raw results without re-measuring
dotnet run -c Release --project GpuBenchmarks -- --recalc
```

Results are written to `GpuBenchmarks/results/` under the working directory.

---

## What it does

Two benchmark tasks, each in four variant families, across multiple sizes and thread
counts.

### Benchmark tasks

| Task | What it computes | Input | Output |
|---|---|---|---|
| **Matrix multiplication** | `C = A × B`, standard O(n³) algorithm | Two N×N `float` matrices | Result N×N matrix |
| **Conway's Game of Life** | 100 simulation steps | N×N `int` grid | Grid state after 100 steps |

### Variants per task

| Variant | How it runs |
|---|---|
| **Sequential** | Single thread, plain `for` loops |
| **Parallel CPU** | `Parallel.For` with 1, 2, 4, 6, 8 and 12 threads |
| **ILGPU** | ILGPU kernel, one GPU thread per output element/cell |
| **ComputeSharp** | ComputeSharp kernel over DirectX 12 |

`Parallel_1` is intentional, not a bug: it measures `Parallel.For` overhead against
plain `Sequential` for small workloads.

### Input sizes

| Task | Sizes |
|---|---|
| Matrix multiply | 64, 128, 256, 512, 1024, 2048, 4096 (N×N) |
| Game of Life | 8, 12, 16, 24, 32, 64, 100, 150, 250, 500, 1000, 2000, 5000, 10000 (N×N) |

### Measurement protocol (per configuration)

1. 3 warmup runs, discarded, to prime JIT and caches
2. 1 probe run to estimate cost and size the measured run count
3. 10 measured runs (reduced to 5 above 30 s per run, 3 above 60 s)
4. Correctness validation on run #1 against the sequential reference
5. Median, mean, min, max, stddev and CV across measured runs

`--quick` shrinks the size lists, thread counts and run counts so the whole suite
finishes in seconds. The CSVs stay fully valid, just with fewer data points.

---

## Weak scaling (Gustafson)

Weak scaling keeps the work per processing element roughly constant while the thread
count grows. Input size scales with thread count `P`:

- **Matrix multiply:** `n = base × P^(1/3)` (work is O(n³))
- **Game of Life:** `n = base × √P` (work is O(n²))

Flags:

- `--weak-scale` enables weak-scaling mode and skips the strong-scaling loops
- `--weak-base <int>` sets the base size (default 256)
- `--weak-gustafson-strict` additionally measures sequential time at each scaled size,
  so the Gustafson speedup is computed on the same workload

Output goes to `weak_raw_results.csv`, `weak_summary.csv` and
`weak_gustafson_analysis.csv`. With strict mode enabled, prefer the `*Strict` columns;
the `Approx*` columns are a weaker "vs base size" ratio, not textbook Gustafson speedup.

`--recalc-weak` regenerates the weak analysis files from saved raw results.

## ILGPU granularity sweep (GPU-vs-GPU)

`--ilgpu-scaling` varies the virtual parallelism of the ILGPU kernel by changing how
many output elements each thread computes (`--ilgpu-ept 64,32,16,8,4,2,1`). It does not
add GPU cores; it is a kernel-configuration study of occupancy versus work per thread.
Speedup in the analysis CSVs is reported against the baseline (largest) EPT. Combines
with `--weak-scale` for weak-scaled sizes, and with `--sizes`, `--gol-sizes`, `--runs`
and `--quick`.

---

## Architecture

```
Program.cs                  Thin entrypoint
  └── Cli/                  Argument parsing (--quick, --weak-scale, --ilgpu-ept, ...)
  └── App/
      └── BenchmarkApplication.cs   Orchestrates strong / weak / ILGPU-sweep / recalc modes
  └── GpuSelector.cs        Device discovery, --gpu flag, GPUBENCH_GPU env var
  └── BenchmarkRunner.cs    Warmup → probe → measure → validate → statistics
  └── IBenchmark            Contract implemented by every benchmark class
        ├── MatrixMultiply/   Sequential, Parallel, ILGPU, ComputeSharp
        └── GameOfLife/       Sequential, Parallel, ILGPU, ComputeSharp
  └── Scaling/              Weak-scaling size math
  └── Reporting/            CSV writers, recalc service
  └── SystemInfo/           CPU/GPU hardware info (WMI on Windows)
```

All runs use a fixed RNG seed (42) so every variant sees identical input data.
Validation compares against the sequential reference: matrices within 1e-2 float
tolerance, Game of Life as an exact integer match.

## Project structure

```
GpuBenchmarks.sln
GpuBenchmarks/              The benchmark application
├── MatrixMultiply/         Per-variant implementations
├── GameOfLife/             Per-variant implementations
├── Cli/  App/  Scaling/  Reporting/  SystemInfo/
└── results/                Measured CSV output (committed sample data)
GpuBenchmarks.Tests/        xUnit: unit + integration tests
setup.ps1                   Windows dependency check + build
merge-results.ps1           Merge raw result CSVs from several machines
verify-results.ps1          Consistency checks across the result CSVs
docs/tech-preview.md        Technology overview (Czech, thesis attachment)
```

## Output files

| File | Contents |
|---|---|
| `raw_results.csv` | Per-run timings |
| `summary.csv` | Median/mean/min/max/stddev/CV and speedups per configuration |
| `amdahl_analysis.csv` | Estimated parallel fraction, theoretical vs actual speedup |
| `parallel1_analysis.csv` | `Parallel.For` overhead baseline |
| `weak_*` | Weak-scaling timings, summary and Gustafson analysis |
| `ilgpu_*` / `weak_ilgpu_*` | Granularity sweep timings and scaling analysis |

Amdahl's parallel fraction is estimated from the measured 6-thread speedup (the physical
core count), then used to predict speedup at other thread counts. The 12-thread row shows
where the model diverges under hyperthreading, which is an expected and explainable
finding.

## GPU support

| Framework | Backend | Requirement |
|---|---|---|
| ILGPU | CUDA, OpenCL, or CPU emulator fallback | Vendor display driver; no NVIDIA required |
| ComputeSharp | DirectX 12 | Windows 10 1903+, DX12-capable GPU |

If no GPU is detected, GPU benchmarks are skipped gracefully and CPU benchmarks still
run. Use `--list-devices` to see what each runtime detects.

## Dependencies

| Package | Version | Purpose |
|---|---|---|
| `ILGPU` / `ILGPU.Algorithms` | 1.5.3 | Cross-platform GPU kernels |
| `ComputeSharp` | 3.1.0 | DirectX 12 compute shaders from C# |
| `System.Management` | 10.0.5 | WMI hardware info (Windows only, conditional reference) |

## Platform notes

- Targets **Windows x64** (`net10.0`, `<Platforms>x64</Platforms>`). Only Windows is
  supported and tested; CI runs on `windows-latest`.
- WMI hardware queries are Windows-only and guarded by
  `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)`.
- Run in **Release** mode for meaningful numbers.

## Tests

```powershell
dotnet test -c Release GpuBenchmarks.sln
```

xUnit suite covering runner statistics, CLI parsing, weak-scaling math, CPU validation
and end-to-end smoke runs.

## Thesis

This suite is the practical component of my master's thesis
*Parallelization of Computations Using Graphics Cards*
(Silesian University in Opava, 2026, supervised by doc. Ing. Jan Górecki, Ph.D.):

- [Thesis (PDF)](docs/thesis.pdf) — in Czech, with an English abstract
- [Defense slides (PPTX)](docs/thesis-defense-slides.pptx)

> **Abstract.** This thesis explores the possibilities of parallelizing computations. The
> theoretical section describes the basic principles of parallel computing, the differences
> between sequential and parallel approaches, types of parallelism, as well as Amdahl's and
> Gustafson's laws. It also describes GPU architecture and provides an overview of available
> technologies for GPU computing, including CUDA, OpenCL, and tools for C#/.NET. The
> practical section implements two computationally intensive tasks, matrix multiplication
> and the Game of Life cellular automaton, in three variants: sequential CPU, parallel CPU,
> and GPU using the ILGPU and ComputeSharp libraries in C#. The thesis presents experiments
> on weak and strong scalability for the CPU variant, which are compared with predictions
> based on Amdahl's and Gustafson's laws. For the GPU variant, the thesis measures
> performance dependencies on input data size, compares measured data from both C#
> libraries, and identifies crossover points between CPU and GPU.

## License

MIT, see [LICENSE](LICENSE).
