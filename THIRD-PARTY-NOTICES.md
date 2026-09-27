# Third-party notices

Tau is licensed under the Apache License 2.0 (see `LICENSE` and `NOTICE`). It depends on, downloads or measures the third-party work below, each under its own licence.

Checked on 2026-09-27. NuGet licences come from each package's nuspec in the local package cache. Python licences come from the installed packages' metadata in `sidecar/finetune/.venv`. Model and dataset licences come from the Hugging Face API (`cardData.license`) at the time of the check, and the source revisions are pinned in `models.lock.json` and `examples/*/dataset.manifest.json`.

Nothing in this list is committed to the repository except as a package reference. Model weights, dataset rows, native libraries and Python packages are downloaded by the scripts from their original sources.

## NuGet packages (Directory.Packages.props)

### Shipped in the Runtime, the client or the `tau` tool

| Package | Version | Licence | Source |
| --- | --- | --- | --- |
| Microsoft.ML.OnnxRuntime.Managed | 1.24.4 | MIT | https://github.com/microsoft/onnxruntime |
| Tokenizers.DotNet | 1.4.1 | MIT | https://github.com/sappho192/Tokenizers.DotNet |
| Tokenizers.DotNet.runtime.win-x64 | 1.4.1 | MIT | https://github.com/sappho192/Tokenizers.DotNet |
| Tokenizers.DotNet.runtime.linux-x64 | 1.4.1 | MIT | https://github.com/sappho192/Tokenizers.DotNet |
| YamlDotNet | 18.1.0 | MIT | https://github.com/aaubry/YamlDotNet |
| OpenTelemetry.Extensions.Hosting | 1.19.1 | Apache-2.0 | https://github.com/open-telemetry/opentelemetry-dotnet |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | 1.19.1 | Apache-2.0 | https://github.com/open-telemetry/opentelemetry-dotnet |
| OpenTelemetry.Instrumentation.AspNetCore | 1.19.0 | Apache-2.0 | https://github.com/open-telemetry/opentelemetry-dotnet-contrib |
| OpenTelemetry.Exporter.Prometheus.AspNetCore | 1.19.1-beta.1 | Apache-2.0 | https://github.com/open-telemetry/opentelemetry-dotnet |

Transitive packages in the same builds: OpenTelemetry, OpenTelemetry.Api and OpenTelemetry.Api.ProviderBuilderExtensions 1.19.1 (Apache-2.0), and System.Numerics.Tensors 9.0.0 (MIT, https://github.com/dotnet/runtime).

Tokenizers.DotNet wraps the Hugging Face `tokenizers` library (Apache-2.0, https://github.com/huggingface/tokenizers).

### ONNX Runtime native libraries (fetched by `scripts/fetch-natives.ps1`)

These are pinned in `Directory.Packages.props` and downloaded from nuget.org. Only their native libraries are extracted, into `native/`, which is gitignored.

| Package | Version | Licence | Source |
| --- | --- | --- | --- |
| Microsoft.ML.OnnxRuntime | 1.24.4 | MIT | https://github.com/microsoft/onnxruntime |
| Microsoft.ML.OnnxRuntime.Gpu.Windows | 1.24.4 | MIT | https://github.com/microsoft/onnxruntime |
| Microsoft.ML.OnnxRuntime.Gpu.Linux | 1.24.4 | MIT | https://github.com/microsoft/onnxruntime |
| Microsoft.ML.OnnxRuntime.DirectML | 1.24.4 | MIT | https://github.com/microsoft/onnxruntime |
| Microsoft.AI.DirectML | 1.15.4 | Microsoft Software License Terms (proprietary) | https://aka.ms/DirectML |

Each ONNX Runtime package carries its own `ThirdPartyNotices.txt`, which travels with the natives.

DirectML is not open source. Its licence allows use on Windows and redistribution inside applications you build, and it forbids distributing it on its own. `scripts/publish.ps1` copies `DirectML.dll` next to the Runtime for the `directml` flavour, which is the redistribution the licence allows.

### Tests and tools only (not shipped)

| Package | Version | Licence | Source |
| --- | --- | --- | --- |
| JsonSchema.Net | 9.4.0 | MIT source, binaries under the Open Source Maintenance Fee EULA (see below) | https://github.com/json-everything/json-everything |
| xunit.v3 | 4.0.1 | Apache-2.0 | https://github.com/xunit/xunit |
| xunit.runner.visualstudio | 4.0.0 | Apache-2.0 | https://github.com/xunit/visualstudio.xunit |
| Microsoft.NET.Test.Sdk | 18.10.1 | MIT | https://github.com/microsoft/vstest |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.12 | MIT | https://github.com/dotnet/aspnetcore |

JsonSchema.Net pulls in Json.More.Net 3.0.1 and JsonPointer.Net 7.0.2 (same terms as JsonSchema.Net) and Humanizer.Core 3.0.10 (MIT).

**JsonSchema.Net's binary terms.** Since version 8 the json-everything packages on nuget.org ship with `OSMFEULA.txt`, an "Open Source Maintenance Fee" agreement. The source stays MIT. The agreement asks users of the pre-built binaries for a monthly fee if they use them in revenue-generating work and have annual gross revenue of US$10,000 or more, and it says the MIT licence governs where the two conflict. Tau uses these packages only in `tests/` and `tools/Tau.Conformance`, so nothing Tau ships contains them, and there's no conflict with Apache-2.0. Whether the fee applies to Fortitude Omnis running the tests is a business question this audit can't settle.

## Python packages (sidecar/finetune)

The sidecar exports the models to ONNX, runs the reference parity checks, prepares the datasets and trains the fine-tunes. It isn't part of the Runtime. Top-level dependencies from `pyproject.toml`, with the versions `uv.lock` resolved:

| Package | Version | Licence | Source |
| --- | --- | --- | --- |
| torch | 2.11.0 (cu128 build) | BSD-3-Clause | https://pytorch.org |
| transformers | 5.17.0 | Apache-2.0 | https://github.com/huggingface/transformers |
| laya | 0.3.20 | Apache-2.0 | https://huggingface.co/convaiinnovations/laya |
| von-sdk | 1.2.3 | Apache-2.0 (bundled `LICENSE.md`) | https://github.com/wfzyx/von |
| onnx | 1.23.0 | Apache-2.0 | https://onnx.ai |
| onnxruntime | 1.30.0 | MIT | https://onnxruntime.ai |
| onnxscript | 0.7.2 | MIT | https://github.com/microsoft/onnxscript |
| safetensors | 0.8.0 | Apache-2.0 | https://github.com/huggingface/safetensors |
| huggingface_hub | 1.33.0 | Apache-2.0 | https://github.com/huggingface/huggingface_hub |
| numpy | 2.5.3 | BSD-3-Clause AND 0BSD AND MIT AND Zlib AND CC0-1.0 | https://numpy.org |
| datasets | 5.0.1 | Apache-2.0 | https://github.com/huggingface/datasets |
| pandas | 3.0.6 | BSD-3-Clause | https://pandas.pydata.org |
| pytest (dev) | 9.1.1 | MIT | https://docs.pytest.org |

## CUDA libraries (fetched by `scripts/fetch-cuda.ps1`)

The CUDA provider needs NVIDIA's CUDA 12.8 runtime, cuBLAS, cuFFT, cuRAND and cuDNN 9.10. The script installs them from NVIDIA's pip wheels into `native/cuda-deps/`, which is gitignored. Their licence is NVIDIA's proprietary software licence (the `License.txt` in each wheel). Tau doesn't redistribute them. `scripts/publish.ps1 -IncludeCudaDeps` copies them into a local publish folder, so read NVIDIA's terms before you share a folder built that way. The CUDA container flavour builds on `nvidia/cuda:12.8.1-cudnn-runtime-ubuntu24.04`, which carries NVIDIA's own container licence.

## Container base images

The Dockerfile builds on `mcr.microsoft.com/dotnet/sdk:10.0`, `mcr.microsoft.com/dotnet/aspnet:10.0` (.NET is MIT) and `mcr.microsoft.com/powershell` (MIT). The base images contain Linux distribution packages under their own licences.

## Models (models.lock.json)

Weights are downloaded from Hugging Face at the pinned revision and never committed.

| Model | Source @ revision | Licence |
| --- | --- | --- |
| laya-en, laya-multilingual, laya-typed-decisions | `convaiinnovations/laya` @ `55cf4c4ebb4ebe31b2550e8bdf3bd21b99753851` | Apache-2.0 |
| von-1.2.0 | `wfzyx/von` @ `5df8185a4f2327ad0a7cd117cc4f701ac557b9ae` | Apache-2.0 |
| MiniLM-L6 baseline | `sentence-transformers/all-MiniLM-L6-v2` @ `1110a243fdf4706b3f48f1d95db1a4f5529b4d41` | Apache-2.0 |

The Laya and Von checkpoints are built on ModernBERT (`answerdotai/ModernBERT-base`, Apache-2.0) and mmBERT (`jhu-clsp/mmBERT`, MIT), per their model cards.

The two local fine-tunes (`laya-en-ft-banking77`, `laya-en-ft-tickets`) are derived from laya-en and aren't committed. The support-tickets fine-tune is trained on CC-BY-NC-4.0 data, so treat that one as non-commercial.

## Datasets

| Dataset | Source @ revision | Licence | What the repository holds |
| --- | --- | --- | --- |
| Banking77 (Casanueva et al., 2020, PolyAI) | `PolyAI-LDN/task-specific-datasets` @ `9d081458ff52e53cf7e848f414e6e9344e4e6696` (GitHub), also `PolyAI/banking77` on Hugging Face | CC-BY-4.0 | Split hashes, per-item predictions keyed by item id, frontier answers, calibrators and reports. No text rows. |
| Customer support tickets (Tobi-Bueck on Hugging Face) | `Tobi-Bueck/customer-support-tickets` @ `ddf1c81a5475992c4fa6752bf1e8b4e31f07bbeb` (Hugging Face) | CC-BY-NC-4.0 | Measurements and calibrators only: split hashes, per-item predictions keyed by item id, frontier answers and reports. No ticket rows or ticket text. |

Banking77 attribution: I. Casanueva, T. Temčinas, D. Gerz, M. Henderson and I. Vulić, "Efficient Intent Detection with Dual Sentence Encoders", NLP4ConvAI workshop, 2020. Data licensed under CC BY 4.0.

The support-tickets data is licensed for non-commercial use only. The scripts download it to `data/`, which is gitignored. A check on 2026-09-27 found none of 2,000 sampled ticket texts in any tracked file or anywhere in the git history. The dataset card says the tickets are synthetic.

## Result of the check

- **No licence in this list conflicts with Tau's Apache-2.0 licence** for what the repository contains and what the Runtime, `Tau.Client` and `tau` ship.
- **Two items carry terms beyond an open-source licence,** and neither is shipped from this repository: DirectML (proprietary, redistributable inside an application) and the NVIDIA CUDA libraries (proprietary).
- **JsonSchema.Net's maintenance-fee EULA** applies to its pre-built binaries, used here only in tests and tools. Whether a fee is owed is for Rob to decide.
- **The support-tickets dataset is non-commercial.** Publishing measurements about it is consistent with CC-BY-NC-4.0 given attribution. Using the tickets fine-tune or the data in commercial work isn't.
- **Not confirmed:** the licences of the transitive Python packages beyond the top-level list above, and the Linux packages inside the container base images. They weren't audited one by one.
