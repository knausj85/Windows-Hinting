# OCR- and local-AI-driven detection of clickable and scrollable regions (UIA fallback)

**Status:** research findings only. No code was changed and nothing was benchmarked.
**Date:** 2026-10-13
**Question:** What can find (a) clickable regions and (b) scrollable regions on the Windows desktop from pixels, with high performance and low latency, when UIA is empty or thin? Typical cases are Chromium/Electron canvases, games, remote desktops and custom-drawn apps.

**Builds on:** `docs/research/cv-clickable-scrollable-detection.md` (branch `research/cv-element-detection`, 2026-09-17). That note surveyed clickable detectors and ranked them by maintenance and license. This note adds what it lacked:
- latency and the hardware it was measured on;
- how each option deploys on Windows and from .NET;
- OCR engines;
- the inference and capture stack;
- scrollable-region detection.

It also uses `docs/research/uia-scroll-signals.md` (branch `research/uia-scroll-signals`) for which apps lack UIA scroll signals.

**How to read the latency numbers:**
- **[V]** means the vendor or author published the number, on their own hardware. Nearly every number here is [V].
- **[I]** means someone else measured it independently. **I found no primary-source [I] numbers for any option in scope**:
  - The engine-comparison posts that turn up in search (Tesseract vs PaddleOCR vs EasyOCR, and similar) don't disclose hardware or method, so I excluded them.
  - I found no peer-reviewed benchmark of OCR or detectors on desktop screenshots.

  Every recommendation below therefore has to be confirmed by a local benchmark (see [Open questions](#9-open-questions-that-need-a-hands-on-benchmark)).

---

## Bottom line

1. **Under ~100 ms, two things are realistic today, both on CPU:**
   - **OCR text boxes:**
     - `Windows.Media.Ocr` is in the box and needs no model.
     - PP-OCRv5-mobile run through ONNX Runtime (via `RapidOcrNet` or `Sdcb.PaddleOCR`) gives confidence and polygons.
   - **A YOLO-nano-class "interactable" detector**, for example OmniParser's `icon_detect`. That model is in fact a **YOLOv8n** with a single class.

   Both only hold if you **crop to the region of interest**: the focused window, or the parts UIA left empty.
   - The vendor CPU numbers are roughly 40–80 ms for a 640-px detector pass, and roughly 58 ms det + ~21 ms per batch of text lines for PP-OCRv5-mobile (FP32, server Xeon) [V].
   - A whole 4K frame will not fit.
2. **Grounding VLMs (ShowUI, UI-TARS, OS-Atlas, GUI-Actor, Phi-Ground, Fara) are the wrong tool for enumerating hints.** They answer one query with one point, and none publishes a latency under 100 ms on consumer hardware.
   - The fastest small grounder with published timings is **GoClick-L (Florence-2, 0.8B): TTFT 91 ms on a datacenter L20 GPU** [V].
   - Keep VLMs for an optional voice or natural-language "click the thing that says…" lane, gated on NPU or GPU.
3. **The NPU path is real but gated.**
   - The Windows AI `TextRecognizer` runs **only on Copilot+ NPUs**: 40+ TOPS, Windows 11 25H2+, a packaged app with the `systemAIModels` capability. Microsoft publishes no latency for it.
   - Bring-your-own-model on an NPU (QNN or OpenVINO through Windows ML) needs **QDQ INT8/INT16 models** and a pre-compiled EPContext cache, or first load costs seconds to minutes.
4. **No off-the-shelf pixel model labels "scrollable region"**, which confirms the prior note.
   - The best Windows-applicable method is **probe-and-diff**: send one wheel tick, then find the sub-rectangle that translated, using tiled phase correlation or optical flow.
   - It has published prior art in Wu et al., UIST 2023 (92% heuristic accuracy for drag-ability) and ShareX's scrolling capture.
   - Visual scrollbar detection helps, but Windows 11 overlay and "hidden until hover" scrollbars make it a corroborator only.
5. **Merge rule:** do what Microsoft's UFO2 does. UIA is authoritative; add a vision box only if its IoU with **every** UIA box is ≤ 0.1. UFO2 runs vision on every step on an A100. We should run it **only when UIA is thin**.

---

## Comparison at a glance

Latency is [V] unless marked otherwise. Details and sources are in sections 1–6.

| Option | Task | Reported latency, with hardware | Box output | License | .NET / Windows path | Maintenance |
|---|---|---|---|---|---|---|
| Windows.Media.Ocr | OCR | none published | word rects | OS | WinRT, in-box | OS component |
| Windows AI TextRecognizer | OCR | none published; Copilot+ **NPU only** | line/word quadrilaterals + confidence | OS | WinAppSDK 1.7.1+, packaged | active |
| PP-OCRv5 mobile (RapidOcrNet / Sdcb) | OCR | det 58 ms + rec 21 ms per call (Xeon 6271C CPU); 11 + 5 ms (T4) | line polygons (+ word/char) + confidence | Apache-2.0 | ORT NuGet ports | active |
| Tesseract 5 | OCR | ~1.8 s/page (Z420 CPU) | block…symbol + confidence | Apache-2.0 | `TesseractOCR` | active |
| EasyOCR | OCR | none | line quadrilaterals | Apache-2.0 | none | dormant |
| OmniParser `icon_detect` (YOLOv8n) | detector | YOLOv8n generic 80 ms CPU ONNX / ~1 ms A100 @640; full pipeline 0.6–0.8 s A100/4090 | interactable boxes | **AGPL** | ONNX → ORT | active |
| OmniParser `icon_detect_v3` (YOLOv9-E) | detector | none (~22× v8n GFLOPs) | boxes | MIT | ONNX (unverified) | 2026-07 |
| YOLO11n / YOLO26n (self-trained) | detector | 56 / 39 ms CPU ONNX; 1.5 / 1.7 ms T4 TRT | your classes | AGPL | ONNX → ORT | active |
| D-FINE-N / RF-DETR-Nano (self-trained) | detector | 2.1 / 2.3 ms T4 TRT FP16 | your classes | Apache-2.0 | ONNX → ORT | active |
| TargetFinder (YOLO26n, desktop) | detector | ~200 ms end-to-end, M3 Max | 6 UI classes | see paper | ONNX-able | 2026-07 |
| UI-DETR-1 | detector | 0.82 s/img (hardware not stated) | boxes | MIT | RF-DETR ONNX | 2025-11 |
| Florence-2 base/large | detector + OCR | none | OD / OCR-with-region boxes | MIT | `florence2-sharp` + ONNX | 2025 |
| YOLO-World | open-vocab detector | 52–74 FPS V100 (fixed vocabulary) | boxes | GPL-3.0 | ONNX | 2025-02 |
| Grounding DINO 1.5 Edge | open-vocab detector | 75 FPS A100 TRT; 10.7 FPS Orin NX | boxes | API only | — | — |
| GoClick-L (0.8B) | grounding | TTFT 91 ms, L20 GPU | point | MIT | Florence ONNX (unverified) | 2026-05 |
| GUI-Actor / UI-Venus / Phi-Ground / UI-TARS / OS-Atlas / ShowUI / Fara | grounding | none published (iSHIFT ~2.2 s/step) | point / box | MIT / Apache | onnxruntime-genai (Fara has a QNN build) | mixed |
| UFO2 hybrid | UIA + vision merge | +~1 s/step (A100 VM) | UIA + vision at IoU ≤ 0.1 | MIT | Python | active |
| Canny/contour (wl-kbptr) | classical | none published | rects, no labels | see repo | OpenCvSharp (reimplement) | active |
| Probe + phaseCorrelate / DIS flow | scroll | DIS 1.65–3.3 ms at 1024×436, 1 core | moving-tile union | BSD/Apache | OpenCvSharp | — |

---

## 1. OCR engines usable locally on Windows

| Engine | Boxes | Confidence | Latency [V], with hardware | .NET path | License / status |
|---|---|---|---|---|---|
| **Windows.Media.Ocr** (`OcrEngine`) | **Word** `BoundingRect` only. `OcrLine` has no rect, so take the union of its words. No character boxes. | None | **Not published.** | WinRT projection, no NuGet (`net8.0-windows10.0.x`) | In-box since Win10 10240. Needs a language pack. |
| **Windows AI `TextRecognizer`** (Windows App SDK) | Line → word, each a **quadrilateral**. Polygon geometry is an option. | Per word, 0–1 (`MatchConfidence`) | **Not published.** Microsoft says "faster and more accurate than" `OcrEngine`, without numbers. | WinAppSDK 1.7.1+ (stable). Packaged app plus the `systemAIModels` capability. | **Copilot+ NPU only.** Win11 25H2 (26200.7309)+. |
| **PP-OCRv5 mobile** (det + rec) | Line polygons; word/char via the port | Yes | det **57.8 ms CPU / 10.7 ms T4**; rec **21.2 ms CPU / 5.4 ms T4** per call (FP32, Xeon 6271C 8 threads). End-to-end **0.8–1.4 s/image CPU** on doc/scene images including I/O. | `Sdcb.PaddleOCR` 3.3.1 (Paddle or ONNX backend); `RapidOcrNet` 4.2.0 (ORT + SkiaSharp, AOT) | Apache-2.0, active (v6 models out) |
| **RapidOCR** | As PaddleOCR (same models) | Yes | No numbers ("extreme speed") | `RapidOcrNet` (BobLd) | Apache-2.0, v3.10.0 on 2026-10-08 |
| **Tesseract 5 LSTM** | Block, paragraph, line, word, symbol | Yes | ~1.8 s for one doc page (old 4.0-era, HP Z420, OpenMP+AVX) | `TesseractOCR` 5.5.2 (Sicos1977) | Apache-2.0, 5.5.3 on 2026-07 |
| **EasyOCR** | Line quadrilateral | Yes | None published | None (PyTorch only) | Apache-2.0, **dormant** (last release 2024-09) |
| *Reference:* Apple Vision | Line | Yes | "Fast" mode is "designed for real-time", no ms given | n/a (macOS) | — |
| *Reference:* docTR | Page, block, line, word | Yes | det 0.5 s at 1024², i7-11800H CPU | none | Apache-2.0 |
| *Reference:* Surya | Line polygons | Yes | Now a VLM pipeline: 18.9 s median latency, RTX 5090 | none | Weights under modified OpenRAIL-M |

### Windows.Media.Ocr
- **API.** `RecognizeAsync(SoftwareBitmap)` returns an `OcrResult` with `Lines`, `Text` and `TextAngle`. Only `OcrWord` carries a `BoundingRect`. Nothing in the API exposes a confidence.
- **Image size.** `MaxImageDimension` is a static property. The docs do not state its value, so read it at runtime. [MS Learn: OcrEngine](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr.ocrengine) · [OcrLine](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr.ocrline) · [OcrResult](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr.ocrresult)
- **Package identity.** The namespace remarks say it is "only supported for desktop apps with package identity" ([Windows.Media.Ocr](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr)). Microsoft's own PowerToys Text Extractor documents calling it from PowerShell ([PowerToys Text Extractor](https://learn.microsoft.com/en-us/windows/powertoys/text-extractor)), so in practice it may work unpackaged. **This must be verified in our unpackaged app.**
- **Languages.** Language packs are optional Windows capabilities (`Language.OCR~~~en-US`), installed with `Add-WindowsCapability` (same PowerToys page).
- **Prior art in Neru.** Neru's Windows build calls this engine through raw WinRT and makes one target per OCR **line** (the union of its word boxes). It captures with GDI BitBlt and downscales frames that exceed `MaxImageDimension`. It does **not** fall back to OCR automatically when UIA is empty; the strategy is chosen per app. (Summary of local notes on the open-source Neru project; not reproduced here.)

### Windows AI APIs: `Microsoft.Windows.AI.Imaging.TextRecognizer`
- **Hardware.** The API matrix lists Text Recognition as **NPU (Copilot+ PC): available; GPU: not supported; CPU: not supported** ([Windows AI APIs overview](https://learn.microsoft.com/en-us/windows/ai/apis/)). Copilot+ means an NPU with more than 40 TOPS ([NPU devices](https://learn.microsoft.com/en-us/windows/ai/npu-devices/)).
- **OS and packaging.** It needs Windows 11 25H2 build 26200.7309+, a packaged app and the `systemAIModels` capability. Without them it throws `UnauthorizedAccessException` ([troubleshooting](https://learn.microsoft.com/en-us/windows/ai/apis/troubleshooting)).
- **Lifecycle.** Call `GetReadyState()`, then `EnsureReadyAsync()` (this downloads the model through Windows Update), then `CreateAsync()`, then `RecognizeTextFromImage(ImageBuffer)`. On `NotSupportedOnCurrentSystem` you must fall back ([text recognition](https://learn.microsoft.com/en-us/windows/ai/apis/text-recognition), [get started](https://learn.microsoft.com/en-us/windows/ai/apis/get-started), [API reference](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.ai.imaging.textrecognizer)).
- **Status.** It has been stable since WinAppSDK 1.7.1. No regional restriction is listed; only Phi Silica and Image Description say "not in China".
- **What this means for us.** It is a nice optional tier on Copilot+ machines. It conflicts with our currently unpackaged distribution, and it cannot be the baseline.

### PP-OCRv5 / RapidOCR / Sdcb
- **Per-module tables.** Measured on a T4 GPU and a Xeon Gold 6271C (8 threads, FP32). "HiPerf" combines TensorRT, mixed precision and MKL-DNN tuning [V] ([PaddleOCR OCR pipeline](https://www.paddleocr.ai/main/en/version3.x/pipeline_usage/OCR.html), [text_detection module](https://www.paddleocr.ai/main/en/version3.x/module_usage/text_detection.html), [text_recognition module](https://www.paddleocr.ai/main/en/version3.x/module_usage/text_recognition.html)):

  | Module | GPU std / HiPerf | CPU std / HiPerf | Size | Accuracy |
  |---|---|---|---|---|
  | PP-OCRv5_mobile_det | 10.67 / 6.36 ms | 57.77 / 28.15 ms | 4.7 MB | hmean 79.0 |
  | PP-OCRv5_mobile_rec | 5.43 / 1.46 ms | 21.20 / 5.32 ms | 16 MB | acc 81.29 |
  | PP-OCRv5_server_det | — | 383 ms | — | — |

  - The input size for these per-module tables is not stated.
  - Recognition runs **per text-line crop, which are batched**. On a text-dense UI the recognition stage dominates.
- **End-to-end** on 200 document and scene images: OCR-mobile **1.15–1.39 s on CPU**, 0.27–0.55 s on GPU, including image I/O [V] ([PP-OCRv5 page](https://www.paddleocr.ai/v3.5.0/en/version3.x/algorithm/PP-OCRv5/PP-OCRv5.html)). These are large document images, not cropped UI regions.
- **v6.** The PP-OCRv6 paper lists PP-OCRv5_mobile end-to-end at 0.80 s on Xeon, and 0.78 s with OpenVINO [V] ([arXiv 2606.13108](https://arxiv.org/pdf/2606.13108)). PP-OCRv6 adds a tiny det model (1.9 MB, hmean 80.6) with no timings yet.
- **.NET ports:**
  - **`RapidOcrNet`** 4.2.0, 2026-09 ([GitHub](https://github.com/BobLd/RapidOcrNet)). It runs on `Microsoft.ML.OnnxRuntime` with SkiaSharp and no OpenCV, targets net8/net10 and is AOT-compatible. It ships PP-OCRv5 Latin mobile ONNX and returns line boxes with optional word/char polygons plus confidences.
  - **`Sdcb.PaddleOCR`** 3.3.1 ([PaddleSharp](https://github.com/sdcb/PaddleSharp)). It has Paddle-Inference (MKL-DNN), ONNX and CUDA backends.
  - Neither port publishes timings.
- **Accuracy on UI or screenshot text is not published** for any engine. PaddleOCR's numbers come from its own document and scene evaluation set.

### Tesseract
- **Speed.** About 1.8 s real time per Hindi page on an HP Z420 with OpenMP+AVX. These are the 4.0-era numbers [V] ([tessdoc 4.0 accuracy & performance](https://tesseract-ocr.github.io/tessdoc/tess4/4.0-Accuracy-and-Performance.html)).
- **Screen text needs preprocessing.** The docs say it "works best on images which have a DPI of at least 300 dpi" and to "use dark text on light background" for 4.x ([ImproveQuality](https://tesseract-ocr.github.io/tessdoc/ImproveQuality.html)). Screen text at 96 DPI therefore needs upscaling, and dark themes need inverting.
- **Verdict:** wrong tool for a 100 ms overlay.

---

## 2. Local UI-element detection models

### 2a. Detectors (fast; boxes for every element)

| Model | Size / input | Latency [V], with hardware | Output | License | Windows path | Status |
|---|---|---|---|---|---|---|
| **OmniParser `icon_detect`** (v1/v2) | **YOLOv8n** (`depth 0.33 / width 0.25`, `nc: 1`), trained at `imgsz 1280`, demo default **640** | No detector-only timing published. YOLOv8n generic: **80.4 ms CPU ONNX, 0.99 ms A100 TRT @640** (Ultralytics). End-to-end with OCR and Florence captions: **0.6 s A100 / 0.8 s RTX 4090**. | Boxes, one "interactable" class | **AGPL-3.0** | Ultralytics `export` → ONNX → ORT | Repo active (2026-07) |
| **OmniParser `icon_detect_v3`** | **YOLOv9-E**, 281 MB TorchScript (YOLOv9e is 58.1M params, 192.5 GFLOPs @640) | **None published**. Roughly 22× YOLOv8n's GFLOPs. | Boxes | **MIT** | MultimediaTechLab/YOLO → ONNX (unverified) | Added 2026-07 |
| **YOLO11n / YOLO26n** (train your own) | 2.6M / 2.4M params; 6.5 / 5.5 GFLOPs | **56.1 / 38.9 ms CPU ONNX**; 1.5 / 1.7 ms T4 TRT (COCO, 640) | Your classes | AGPL (Ultralytics) | ONNX → ORT | Active |
| **D-FINE-N**, **RF-DETR-Nano** | 4M / 30.5M params | 2.12 ms / 2.3 ms on T4 TRT FP16 (no CPU numbers) | Your classes | Apache-2.0 | ONNX export | Active |
| **TargetFinder** (2026-07) | YOLO26n @640, 520 desktop (Win/mac/Ubuntu/web) screenshots | **~200 ms end-to-end on M3 Max, 10–24 FPS, 4–7% CPU** | 6 classes (Button, ToggleButton, TextInput, Slider, Text, HyperLink). F1 0.885 vs OmniParser 0.698. | see paper | ONNX-able | New |
| **UI-DETR-1** (racineai) | RF-DETR-M fine-tune, input **1600** | "0.82 s/image vs OmniParser 0.77 s", **hardware not stated** | Boxes; WebClick 70.8% vs OmniParser 58.8% | MIT | RF-DETR ONNX export | 2025-11 |
| **Florence-2** base / large | 0.23B / 0.77B, **fixed 768²** | None published | `<OD>`, `<OCR_WITH_REGION>`, `<REGION_PROPOSAL>` | MIT | ONNX weights (onnx-community) + `florence2-sharp` NuGet | 2025 |
| **YOLO-World** S/M/L | 13–48M | **74 / 58 / 52 FPS on V100 @640** with fixed vocabulary; ~18–20 FPS with the text encoder | Open-vocabulary boxes. **Not trained on UI.** | **GPL-3.0** | ONNX export | V2.1, 2025-02 |
| **OWLv2** base | 0.2B, fixed 960² | None | Text- or image-conditioned boxes | Apache-2.0 | HF / ONNX | — |
| **Grounding DINO 1.5 Edge** | EfficientViT-L1 | 75 FPS A100 TRT; 10.7 FPS Jetson Orin NX @640 | Open-vocabulary boxes | **API only, no weights** | — | Not usable locally |
| **WebUI FCOS** (CHI 2023) | ResNet-50-FPN, 320–640 | None | 25 classes | Research terms | — | Stale (2023-12) |

Sources:
- OmniParser:
  - [model.yaml](https://huggingface.co/microsoft/OmniParser-v2.0/resolve/main/icon_detect/model.yaml)
  - [train_args.yaml](https://huggingface.co/microsoft/OmniParser-v2.0/resolve/main/icon_detect/train_args.yaml)
  - [file tree incl. icon_detect_v3](https://huggingface.co/api/models/microsoft/OmniParser-v2.0/tree/main/icon_detect_v3)
  - [v2 card](https://huggingface.co/microsoft/OmniParser-v2.0)
  - [gradio_demo.py](https://raw.githubusercontent.com/microsoft/OmniParser/master/gradio_demo.py)
  - [paper arXiv 2408.00203](https://arxiv.org/html/2408.00203)
- Ultralytics:
  - [YOLOv8](https://docs.ultralytics.com/models/yolov8/)
  - [YOLO11](https://docs.ultralytics.com/models/yolo11/)
  - [YOLO26](https://docs.ultralytics.com/models/yolo26/)
  - [YOLOv9](https://docs.ultralytics.com/models/yolov9/)
- DETR-family detectors:
  - [D-FINE](https://github.com/Peterande/D-FINE)
  - [RF-DETR](https://github.com/roboflow/rf-detr)
- UI-specific detectors:
  - [TargetFinder arXiv 2607.19907](https://arxiv.org/html/2607.19907)
  - [UI-DETR-1](https://huggingface.co/racineai/UI-DETR-1)
  - [WebUI arXiv 2301.13280](https://arxiv.org/abs/2301.13280), [js0nwu/webui](https://github.com/js0nwu/webui)
- Florence-2:
  - [Florence-2-base](https://huggingface.co/microsoft/Florence-2-base)
  - [onnx-community/Florence-2-base](https://huggingface.co/onnx-community/Florence-2-base)
  - [florence2-sharp](https://github.com/curiosity-ai/florence2-sharp)
- Open-vocabulary detectors:
  - [YOLO-World arXiv 2401.17270](https://arxiv.org/html/2401.17270)
  - [OWLv2](https://huggingface.co/google/owlv2-base-patch16-ensemble)
  - [Grounding DINO 1.5 arXiv 2405.10300](https://arxiv.org/html/2405.10300)

**Correction to the prior note.** That note recommended `icon_detect_v3` as "the" choice. It is MIT-licensed, but it is a **YOLOv9-E** (~58M params), not a nano model, and it has no published latency. On CPU it is unlikely to fit 100 ms; this needs benchmarking. If we want a CPU-fast detector with a permissive license, the realistic route is **training a nano model ourselves** (YOLOv9-t/s via the MIT MultimediaTechLab implementation, or D-FINE-N / RF-DETR-Nano under Apache-2.0) on public UI data or OmniParser-style DOM-derived labels.

### 2b. Grounding VLMs (slow; one query → one point or box)

| Model | Params / base | ScreenSpot-Pro (SS / SS-v2) | Latency [V] | License |
|---|---|---|---|---|
| **GoClick-B / -L** | 0.23B / 0.8B, Florence-2 | — (SS 74.1 / 78.5; v2 75.2 / 81.1) | **L: TTFT 91.1 ms, 8.3 ms/token, NVIDIA L20, 780×360** | MIT |
| **GUI-Actor** 2B / 3B / 7B | Qwen2-VL / 2.5-VL | 36.7 / 42.2 / 40.7 | None. Gives **multiple candidates per forward pass** (attention head, no coordinate decoding). | MIT |
| **UI-Venus-1.5-2B** | Qwen3-VL | 57.7 | None | Apache-2.0 |
| **Holo1.5-3B** | Qwen2.5-VL-3B | 51.5 (v2 91.7) | None | **Qwen Research (non-commercial)** |
| **Phi-Ground** | ~4B, Phi-3.5-V, fixed 1008×672 | 43.2 (v2 92.3–93.4 with agent) | None. Paper argues latency ∝ params × image tokens. | MIT |
| **UI-TARS-1.5-7B** / UI-TARS-2B-SFT | Qwen2.5-VL | 49.6 / 18.9 | None | Apache-2.0 |
| **OS-Atlas-Base-4B / 7B** | InternVL2 / Qwen2-VL | — / 18.9 (SS 70.1 / 82.5) | None | Apache-2.0 |
| **ShowUI-2B** | Qwen2-VL-2B | 7.7 (SS 75.1) | None. The 1.4× token-selection speed-up is a **training** claim. | MIT |
| **UGround-V1** 2B / 7B | Qwen2-VL | 7B 16.5 (SS 77.7 / 86.3) | None | MIT |
| **Qwen2.5-VL-3B / 7B** | — | 25.9 / 29.0 | None | Qwen / Apache |
| **Fara-7B** / Fara1.5-4B | Qwen2.5-VL / Qwen3.5 | — | None. **Fara-7B-onnx is an int4 QNN NPU build for onnxruntime-genai + WinML.** | MIT |
| Ferret-UI / 2 / Lite | Apple | Lite 53.3 | None | CC-BY-NC, Lite weights unreleased |
| iSHIFT (~2.5B) | Qwen2-VL-2B + DINOv2 | — | **~2.2 s/step** (GPU implied A100) | code unreleased |

Sources:
- Grounders, part 1:
  - [GoClick arXiv 2604.23941](https://arxiv.org/html/2604.23941), [GoClick repo](https://github.com/ZJULiHongxin/GoClick)
  - [GUI-Actor](https://huggingface.co/microsoft/GUI-Actor-2B-Qwen2-VL), [GUI-Actor arXiv 2506.03143](https://arxiv.org/html/2506.03143)
  - [UI-Venus-1.5-2B](https://huggingface.co/inclusionAI/UI-Venus-1.5-2B)
  - [Holo1.5-3B](https://huggingface.co/Hcompany/Holo1.5-3B)
  - [Phi-Ground](https://huggingface.co/microsoft/Phi-Ground), [Phi-Ground arXiv 2507.23779](https://arxiv.org/html/2507.23779)
  - [UI-TARS-1.5-7B](https://huggingface.co/ByteDance-Seed/UI-TARS-1.5-7B)
- Grounders, part 2:
  - [OS-Atlas arXiv 2410.23218](https://arxiv.org/html/2410.23218)
  - [ShowUI arXiv 2411.17465](https://arxiv.org/html/2411.17465)
  - [UGround](https://github.com/OSU-NLP-Group/UGround)
  - [Qwen2.5-VL-7B](https://huggingface.co/Qwen/Qwen2.5-VL-7B-Instruct)
  - [Fara-7B-onnx](https://huggingface.co/microsoft/Fara-7B-onnx), [microsoft/fara](https://github.com/microsoft/fara)
  - [Ferret-UI Lite arXiv 2509.26539](https://arxiv.org/html/2509.26539v1), [apple/ml-ferret](https://github.com/apple/ml-ferret)
  - [iSHIFT arXiv 2512.22009](https://arxiv.org/html/2512.22009v1)
- Benchmarks and runtime:
  - ScreenSpot-Pro cross-model numbers: [arXiv 2504.07981](https://arxiv.org/html/2504.07981)
  - C#/Windows VLM runtime: [onnxruntime-genai](https://github.com/microsoft/onnxruntime-genai) (v0.17, 2026-09; CPU/CUDA/DML/TRT-RTX/OpenVINO/QNN/WebGPU)

**Takeaway.** None of these enumerates all clickables, and no 2–4B grounder publishes a latency on consumer GPU, CPU or NPU. They belong in a **voice-query lane** ("click Submit") as a fallback **after** OCR string match fails. GoClick, which is Florence-2-sized and might run through `florence2-sharp`, and GUI-Actor, with its multiple candidates per pass, are the most overlay-friendly.

### 2c. Microsoft UFO2: a hybrid of UIA and vision
- **What it is.** UFO2 runs OmniParser-v2 (YOLOv8 + Florence-2) alongside UIA. It **discards vision boxes whose IoU with any UIA control exceeds 10%** and wraps the survivors as pseudo-UIA controls ([arXiv 2504.14603](https://arxiv.org/html/2504.14603)).
- **Gain.**
  - On WAA, task success rises from 23.4% (UIA-only) to 26.6% (hybrid), recovering 9.9% of UIA-only failures.
  - On OSWorld-W, hybrid recovers 12.5% of UIA-only failures.
- **Cost.** About **1 s per step** for OmniParser, which ran on a separate **A100 80 GB** VM. LLM inference (about 10 s per step) dominates.
- **Code.** In [microsoft/UFO](https://github.com/microsoft/UFO), `CONTROL_BACKEND: ["uia"]` is the default; you add `"omniparser"` to enable hybrid.
  - Defaults: `IOU_THRESHOLD_FOR_MERGE: 0.1`, `IMGSZ: 640`, `BOX_THRESHOLD: 0.05`.
  - `merge_control_list` keeps all UIA controls and appends a vision control only if its IoU is ≤ 0.1 against every UIA control. It is a plain O(N·M) check with no containment test.
  - Vision runs **on every step**, not only when UIA is empty.
- **Lesson for us.** Copy the merge rule. Do **not** copy the "always run vision" trigger.

---

## 3. Classical CV (edges, contours, MSER, templates)

- **wl-kbptr.** Its pipeline is grayscale → `Canny(70,220)` → `dilate` → `findContours(RETR_TREE)` → size filters → hierarchy pruning. The pruning drops icon strokes, same-centre children and children of small squarish parents ([target_detection.cpp](https://github.com/moverest/wl-kbptr/blob/main/src/target_detection.cpp)).
  - Neru ports the same pipeline to pure Go as its `contour` strategy.
  - Neru's `vision` strategy runs OCR and contours together on one frame, drops rectangles that overlap text at IoU ≥ 0.5, and lets text win NMS. (Short summary of a local note on Neru.)
  - **Neither publishes timings.**
- **Accuracy.** Chen et al., ESEC/FSE 2020, compared seven methods on 50k+ Rico screens. F1 at IoU > 0.9 [V]:

  | Method | Type | F1 |
  |---|---|---|
  | REMAUI | classical | 0.201 |
  | Xianyu | classical | 0.154 |
  | Faster R-CNN | deep | 0.438 |
  | YOLOv3 | deep | 0.383 |
  | CenterNet | deep | 0.401 |

  - Classical boxes are "fairly accurate" when found, but recall is poor.
  - A hybrid of classical non-text detection plus deep OCR beat both.
  - **No runtimes reported** ([arXiv 2008.05132](https://arxiv.org/html/2008.05132)).
  - TargetFinder (2026) reports REMAUI at F1 0.455 against 0.885 for its YOLO26n ([arXiv 2607.19907](https://arxiv.org/html/2607.19907)).
- **MSER.** OpenCV implements linear-time MSER (Nistér & Stewénius, ECCV 2008). The only throughput figure is about 1.5–2 MP/s on a 1.2 GHz Centrino, from an old OpenCV wiki [V, old hardware] ([Springer](https://link.springer.com/chapter/10.1007%2F978-3-540-88688-4_14)).
- **Latency.** No primary source gives Canny, contour or `matchTemplate` timings at 1080p or 4K. They are generally assumed to be single-digit to tens of ms on one CPU core, but **that assumption has not been verified here**.
- **When it is good enough:**
  - flat, high-contrast, bordered UIs (classic Win32, RDP of classic apps, many games' menus);
  - as a **cheap prefilter** that proposes boxes, which OCR then labels;
  - when no model download is acceptable.
- **When it is not:** borderless modern UIs such as Fluent, flat web and icon-only toolbars. There it loses badly on recall to learned detectors.

---

## 4. Scrollable regions without UIA

Context: `docs/research/uia-scroll-signals.md` found that `ScrollPattern` is blind in three cases:
- terminals (always);
- canvas, transform-based and virtualized web surfaces;
- older Chromium.

So a pixel path has real targets.

### 4a. Datasets and models
- **No released model takes a screenshot and labels "scrollable region"**, and **no desktop/Windows dataset has scroll-container labels**. This confirms and sharpens the prior note.
- **Rico (Android).** View-hierarchy nodes carry `scrollable-horizontal` / `scrollable-vertical` ([Rico archive](https://www.interactionmining.org/archive/rico)). These come from the hierarchy, not pixels, and no detector trained on them has been published.
- **AMEX (Android).** 104k screenshots and 1.66M elements, CC-BY-4.0. It **explicitly annotates scrollable elements**, parsed from Appium XML and human-checked. It claims "no existing dataset provides information on the 'scroll' action" ([arXiv 2407.17490](https://arxiv.org/html/2407.17490)). This is the best pixel-aligned scroll-label source available, but it is mobile only.
- **WebUI.** 400k pages with computed CSS stored. CSS `overflow` is probably recoverable, which would give a **web scroll-container label set**, but this is unverified ([arXiv 2301.13280](https://arxiv.org/html/2301.13280)).
- **SwipeGen (2026, Android).** Uses Qwen3-VL-4B to "directly infer scrollable regions from GUI screenshots" when no node exists. The accuracy of that step is not evaluated ([arXiv 2601.18305](https://arxiv.org/html/2601.18305)).
- **Agent action spaces.** UI-TARS has `scroll(point, direction)`, so the VLM picks the point ([prompt.py](https://github.com/bytedance/UI-TARS/blob/main/codes/ui_tars/prompt.py)). AndroidControl uses direction only. Mind2Web has no scroll. None localizes a container.
- **Taxonomies without a scroll class:** Ferret-UI (scroll interactions are listed as future work, [arXiv 2404.05719](https://arxiv.org/html/2404.05719)), VINS, ScreenAI, TargetFinder.

### 4b. Visual scrollbar cues
- **WinUI.** WinUI scrollbars are "conscious of input method".
  - After touch they show a ~2 px *panning indicator*. On mouse hover they become a ~6 px scrollbar, overlaid 16 px over the content.
  - With `ScrollBarVisibility.Visible` the bar shows only on hover unless content overflows.
  - With `Hidden` there is **no bar at all, yet the wheel still scrolls**.
  - Source: [MS Learn: scroll controls](https://learn.microsoft.com/en-us/windows/apps/design/controls/scroll-controls).
- **Chromium on Windows** draws Fluent (non-overlay) scrollbars by default. `kOverlayScrollbar` is enabled by default only on Android, ChromeOS, Fuchsia, iOS and Mac ([native_theme_features.cc](https://chromium.googlesource.com/chromium/src/+/main/ui/native_theme/features/native_theme_features.cc)). Edge's default was not verified.
- **The Windows "Always show scrollbars" setting** (Accessibility → Visual effects) is documented only by secondary sources. The `DynamicScrollbars` registry value is unverified.
- **No published paper on scrollbar detection** was found. A patent ([US11886327B2](https://patents.google.com/patent/US11886327)) notes modern scrollbars are "a visual slot without any arrows".
- **Verdict:** a thin-rectangle detector for the track or thumb (classical, or one extra class in a self-trained detector) is a **positive corroborator only**. A missing bar is not evidence that the region doesn't scroll.

### 4c. Probe-and-diff (the strongest option)
- **Prior art 1: Wu et al., "Never-ending UI Learner", UIST 2023 (iOS).**
  - The crawler performs real drags, then template-matches each element's patch between the pre- and post-drag screenshots, on grayscaled and edge-detected images. It also finds co-moving boxes.
  - Heuristic accuracy is **0.92** on 1,000 human-validated samples.
  - A pixel-only model trained on those labels reached F1 0.794, because draggability cues are "often non-local".
  - Source: [arXiv 2308.08726](https://arxiv.org/html/2308.08726).
- **Prior art 2: ShareX scrolling capture.**
  - Default input is `SendMouseWheel(-120 × 2)`, with a 300 ms start delay and 300 ms between scrolls.
  - The end of scrolling is detected by identical frames or `GetScrollInfo`.
  - Overlap is found by exact row-hash matching that ignores side margins and a static bottom band.
  - It needs a user-chosen region and does not localize the moving sub-rectangle.
  - Sources: [ScrollingCaptureManager.cs](https://github.com/ShareX/ShareX/blob/develop/ShareX.ScreenCaptureLib/ScrollingCaptureManager.cs), [ScrollingCaptureOptions.cs](https://github.com/ShareX/ShareX/blob/develop/ShareX.ScreenCaptureLib/ScrollingCaptureOptions.cs).
- **Motion estimators:**
  - **OpenCV `phaseCorrelate`** returns a sub-pixel global shift plus a `response` value. The response falls when there are multiple peaks, so it works as a confidence measure ([imgproc.hpp](https://github.com/opencv/opencv/blob/4.x/modules/imgproc/include/opencv2/imgproc.hpp)).
  - **DIS optical flow** runs at **1.65 ms (600 Hz) and 3.3 ms (300 Hz)** at its fastest operating points on Sintel 1024×436, single core i7-3770K [V] ([arXiv 1603.03590](https://arxiv.org/abs/1603.03590)). OpenCV ships it as `DISOpticalFlow` (ULTRAFAST / FAST / MEDIUM presets).
- **A localization design (my synthesis; not published, not benchmarked):**
  1. Capture a pre-frame of the ROI.
  2. Send **one** `WM_MOUSEWHEEL` or `SendInput` tick at the candidate point.
  3. Wait for a settle (reuse the chained-hinting settle work).
  4. Capture a post-frame.
  5. Tile both frames (e.g. 64–128 px tiles). Run `phaseCorrelate` per tile, or DIS on a downscaled frame.
  6. Tiles with a consistent `dy ≠ 0` and high response form the viewport. Their bounding union, clipped to the window, is the scroll region.
  7. Scroll back, or accept the side effect.

  Costs and caveats:
  - The probe is **mutating**, so it fits a "verify a candidate" step, or a per-app profile learned once and cached. It does not fit every hint pass.
  - Smooth-scroll animations need the settle wait. Sticky headers and parallax produce mixed tiles.
- **Cheap non-mutating prior.** On thin-UIA surfaces, large OCR-dense rectangles that are clipped at the window or pane edge (text lines cut off at the bottom border) are plausible scroll containers. This is my own heuristic, untested.

---

## 5. Runtime and inference stack on Windows

**ONNX Runtime** ([releases](https://github.com/microsoft/onnxruntime/releases)): the current version is **1.31.0** (2026-10-09). CUDA is now a separate plugin EP.

| EP | Hardware | Notes | NuGet |
|---|---|---|---|
| **CPU** | any | Baseline. "Low-latency for small models" per Windows ML guidance. | `Microsoft.ML.OnnxRuntime` |
| **DirectML** | Any DX12 GPU (NVIDIA Kepler+, AMD GCN1+, Intel Haswell+, Adreno 600+) | **"Sustained engineering"**; new work moved to WinML. Needs `DisableMemPattern` and `ORT_SEQUENTIAL`. No concurrent `Run()` on one session. ([docs](https://onnxruntime.ai/docs/execution-providers/DirectML-ExecutionProvider.html)) | `.DirectML` |
| **QNN** | Snapdragon X Hexagon NPU (ARM64) | **The HTP/NPU runs only quantized QDQ models.** Recommended: uint16 activations, uint8 weights. Context cache via `ep.context_enable`. ([docs](https://onnxruntime.ai/docs/execution-providers/QNN-ExecutionProvider.html)) | `.QNN` (check that win-arm64 binaries are present) |
| **OpenVINO** | Intel CPU, iGPU, NPU | `cache_dir` cuts reload time. The NPU wants static shapes (`reshape_input`). ([docs](https://onnxruntime.ai/docs/execution-providers/OpenVINO-ExecutionProvider.html)) | `Intel.ML.OnnxRuntime.OpenVino` |
| **CUDA / NvTensorRtRtx** | NVIDIA (RTX 30xx+ for TRT-RTX) | TRT-RTX does an AOT compile to EPContext plus a JIT runtime cache, and is "just under 200 MB" ([docs](https://onnxruntime.ai/docs/execution-providers/TensorRTRTX-ExecutionProvider.html)) | plugin EP |

**Windows ML** is generally available with WinAppSDK 1.8.1 (2025-09-23) ([blog](https://blogs.windows.com/windowsdeveloper/2025/09/23/windows-ml-is-generally-available-empowering-developers-to-scale-local-ai-across-windows-devices/)). It is "the Windows-supported and maintained copy of ONNX Runtime" with the same APIs ([overview](https://learn.microsoft.com/en-us/windows/ai/new-windows-ml/overview)).
- **Bundled EPs:** CPU and DirectML (legacy).
- **Downloaded on demand** via `ExecutionProviderCatalog.GetDefault().EnsureAndRegisterCertifiedAsync()`: QNN, OpenVINO, VitisAI, MIGraphX, NvTensorRtRtx and WebGPU. The download "can take multiple seconds or even minutes" on first run ([initialize EPs](https://learn.microsoft.com/en-us/windows/ai/new-windows-ml/initialize-execution-providers), [supported EPs](https://learn.microsoft.com/en-us/windows/ai/new-windows-ml/supported-execution-providers)).
- **OS:** hardware EPs need **Win11 24H2 (26100)+**.
- **Package:** `Microsoft.Windows.AI.MachineLearning` 2.4.89 bundles ORT 1.27.1 and needs .NET 8+ for the full API ([NuGet](https://www.nuget.org/packages/Microsoft.Windows.AI.MachineLearning), [get started](https://learn.microsoft.com/en-us/windows/ai/new-windows-ml/get-started)).
- **Thread spinning** is disabled by default, which saves battery and costs latency. Re-enable it with `session.intra_op.allow_spinning=1` for latency-critical sessions ([run models](https://learn.microsoft.com/en-us/windows/ai/new-windows-ml/run-onnx-models)).
- **Foundry Local** is an LLM runtime and irrelevant for bring-your-own detectors.

**Cold start versus warm runs:**
- Hardware EPs compile the model on first load: "few seconds to several minutes", and "tens of seconds to minutes" on NPUs for larger models. Windows ML "highly recommends pre-compilation" through `OrtModelCompilationOptions` → `CompileModel()` into an EPContext model.
- Before reusing that model, validate it with `GetModelCompatibilityForEpDevices` and accept only `EP_SUPPORTED_OPTIMAL`. Invalidate it when the driver, EP or ORT version changes ([model compilation](https://learn.microsoft.com/en-us/windows/ai/new-windows-ml/model-compilation), [EPContext design](https://onnxruntime.ai/docs/execution-providers/EP-Context-Design.html)).
- For the CPU EP, save the optimized graph with `SessionOptions.OptimizedModelFilePath`. It is tied to the ISA, e.g. AVX2 ([graph optimizations](https://onnxruntime.ai/docs/performance/model-optimizations/graph-optimizations.html)).
- **No vendor publishes cold-start ms figures for YOLO-nano-class models.** Measure them.

**Quantization:**
- ORT recommends **static QDQ S8S8** for CNNs and running `quant_pre_process()` first.
- A speed-up requires VNNI on x86, int8 Tensor Cores on GPU, or dot-product instructions on Arm.
- **ORT publishes no numeric speed-up** ([quantization](https://onnxruntime.ai/docs/performance/model-optimizations/quantization.html)).
- INT8 or INT16 QDQ is **mandatory** for the QNN NPU.

**What runs where:**
- **CPU only** is the baseline we can ship everywhere. Nano detectors are roughly 40–80 ms at 640 [V, unnamed or Xeon CPUs]; PP-OCR mobile det is roughly 58 ms.
- **Any DX12 GPU via DirectML** probably gives 5–10× speed-ups. This is extrapolated from the T4 numbers and is unverified on iGPUs.
- **The NPU** is only for Copilot+ machines and pre-quantized, pre-compiled models.

---

## 6. Capture path

| API | Since | Output | Region / window | Notes |
|---|---|---|---|---|
| **GDI BitBlt** (screen DC → DIB) | always | CPU DIB | any rect | Simple. What Neru uses. |
| **Windows.Graphics.Capture** | Win10 1803; `CreateForWindow/Monitor` interop 1903 | GPU D3D11 surface, frame pool | per window or monitor | `IsBorderRequired=false` needs `RequestAccessAsync(Borderless)` (a user prompt) plus a capability. **24H2 adds `DirtyRegionMode`, `DirtyRegions` and `MinUpdateInterval`.** |
| **DXGI Desktop Duplication** | Win8 | GPU surface | per output | Gives dirty and move rects (apply moves first). `ACCESS_LOST` on mode change or fullscreen. |

Sources:
- Windows.Graphics.Capture:
  - [GraphicsCaptureSession](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscapturesession)
  - [IsBorderRequired](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscapturesession.isborderrequired)
  - [DirtyRegionMode](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscapturesession.dirtyregionmode)
  - [screen capture guide](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture)
  - [2019 capture blog](https://blogs.windows.com/windowsdeveloper/2019/09/16/new-ways-to-do-screen-capture/)
- DXGI Desktop Duplication:
  - [AcquireNextFrame](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_2/nf-dxgi1_2-idxgioutputduplication-acquirenextframe)
  - [Desktop Duplication API](https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/desktop-dup-api)

**Latency.** **Microsoft publishes no ms comparison** of these three, and no first-frame latency for WGC. We need our own benchmark. For a one-shot hint pass on a cropped window, BitBlt is probably adequate. WGC or DDA becomes worthwhile for continuous diffing, such as scroll probes and cache invalidation, because the dirty rects come for free.

**Excluding our overlay.**
- `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)` (Win10 2004+): the window "does not appear at all" in captures. It applies only to top-level windows owned by the caller, only while DWM composes, and is "not a security feature". The docs **do not list which APIs honor it** ([SetWindowDisplayAffinity](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity)).
- `WCA_EXCLUDED_FROM_DDA` excludes a window from DDA only ([window composition attributes](https://learn.microsoft.com/en-us/windows/win32/dwm/windowcompositionattrib)).
- Verify all three capture paths empirically.
- The simpler alternative is to capture **before** showing hints, or while the overlay is hidden.

---

## 7. Latency-reduction patterns

1. **Trigger vision only when UIA is thin.** For example: fewer than N invokable elements within the focused window, or a large client area with no UIA children (canvas, RDP or game window classes). The per-app profile can force vision on or off. Unlike UFO2, never run it on every pass.
2. **Crop to a region of interest:**
   - the focused window's client rect;
   - better, **UIA's empty sub-regions**: subtract the union of UIA bounding rects and run vision only on large leftover rectangles.
   - At the 640-px model input, a cropped window gives far better effective resolution than a downscaled 4K desktop.
3. **Tile rather than downscale on high-DPI.** Small icons vanish at 640 px from 4K. Use 2–4 tiles at native scale, or run det at 640 and rec on full-resolution crops.
4. **Cache and diff:**
   - Hash tiles of the captured frame, or use WGC/DDA dirty rects.
   - Re-run detection or OCR only on changed tiles.
   - Key cached results by window and tile hash.
5. **OCR as label, detector as box.** Run both concurrently on the same frame. Text boxes inside a detector box become that box's label, which is what OmniParser's `remove_overlap_new` and Neru's text-wins NMS both do. Then merge with UIA by **IoU ≤ 0.1 against every UIA rect**, as UFO2 does.
6. **Pre-warm:**
   - Create ORT sessions at startup or first idle and run one dummy inference.
   - Persist the EPContext / optimized model.
   - Call `TextRecognizer.EnsureReadyAsync` at install or first run.
   - Keep the `OcrEngine` instance cached (Neru does).
   - Enable intra-op spinning for latency-critical sessions.
7. **Two-stage hints.** Show UIA hints immediately, then stream in vision hints when ready, at roughly 50–150 ms. The user rarely needs vision targets in the first frame.

---

## 8. Recommended tiered architecture for Windows-Hinting

**Tier 0: UIA (existing).** Always runs first. It is authoritative.

**Tier 1: CPU, with no hardware requirements. Target under ~100 ms on a cropped region. Try first.**
- **Triggering:**
  - Fires when UIA is thin, or on per-app opt-in.
  - Capture uses BitBlt of the focused-window client rect, with the overlay hidden or excluded.
- **OCR choice:**
  - **(a) `Windows.Media.Ocr`** gives word boxes with zero model shipping. It is the first spike, but packaged-identity behaviour must be checked.
  - **(b) PP-OCRv5-mobile via `RapidOcrNet`** if (a) is too slow or inaccurate, or if confidence and polygons are needed.
- **Optional detector:** an **interactable detector** at YOLO-nano scale, ONNX, CPU EP, 640 px, INT8 QDQ. Candidates:
  - OmniParser `icon_detect` (YOLOv8n): best fit today, but **AGPL**, so it suits evaluation only unless the AGPL is acceptable;
  - a self-trained permissive nano model (YOLOv9-t/s MIT, D-FINE-N or RF-DETR-Nano Apache);
  - TargetFinder-style desktop classes.
- **Merge:**
  - detector box plus OCR label, then dedup against UIA at IoU ≤ 0.1;
  - classical contours (wl-kbptr pipeline) as a no-model fallback for bordered UIs.

**Tier 2: optional, accelerator-gated.**
- Windows ML with DirectML on any GPU, or OpenVINO on Intel iGPU/NPU, for the same Tier-1 models. Use a pre-compiled EPContext and validate compatibility.
- Windows AI `TextRecognizer` on Copilot+ NPUs. It needs packaging plus `systemAIModels`.
- QNN NPU for QDQ-quantized detectors on Snapdragon.

**Tier 3: too slow for hinting; voice-query only.**
- OmniParser end-to-end with Florence captions (0.6–0.8 s on a high-end GPU).
- `icon_detect_v3` (YOLOv9-E), until it is benchmarked.
- UI-DETR-1 at 1600 px.
- Florence-2 OD.
- All grounding VLMs: GoClick, GUI-Actor, UI-Venus, Phi-Ground, UI-TARS, Fara via onnxruntime-genai.
- Tesseract, EasyOCR, Surya, docTR.

**Scroll:**
- UIA signals first (prior note).
- Then a scrollbar-affordance corroborator.
- Then the **probe-and-diff verifier** (tiled `phaseCorrelate` or DIS) to confirm and localize. Cache the result per app profile.
- Do not look for a "scrollable" model.

---

## 9. Open questions that need a hands-on benchmark

1. Wall-clock time for `Windows.Media.Ocr` and `RapidOcrNet` (PP-OCRv5 mobile) on cropped windows of 1080p and 4K text-dense UIs (VS Code, Slack, a RDP session) on a typical laptop CPU. Is recognition per line batched?
2. Does `Windows.Media.Ocr` work from our unpackaged process? What is `MaxImageDimension`?
3. OmniParser `icon_detect` (YOLOv8n) exported to ONNX: CPU FP32 vs INT8 latency at 640 and 1280, and recall on our pathological apps. Repeat for `icon_detect_v3` (YOLOv9-E).
4. ORT session cold start versus warm runs, for CPU, DirectML and OpenVINO, with and without saved optimized or EPContext models.
5. BitBlt vs WGC vs DDA: latency for a window crop. Which of them honor `WDA_EXCLUDEFROMCAPTURE`?
6. Probe-and-diff: settle time after one wheel tick in Chromium, WinUI and terminals; whether tiled phase correlation separates the scrolled viewport from sticky headers; and false positives from animations.
7. Do classical contours (wl-kbptr settings) give usable recall on RDP and game menus, and in how many ms at 1080p?
8. TextRecognizer latency on a Copilot+ NPU versus `OcrEngine` on the same machine.
9. Licensing decision: is an AGPL detector acceptable as an optional download, or must we train a permissive nano model? If we train, which data: OmniParser-style DOM-derived labels, WebUI, or TargetFinder's set?
