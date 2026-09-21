# Milestone 2 — local English OCR

Implementation is ready for Windows validation. Translation remains Chinese filler;
Luna integration is milestone 3.

## Behavior

- Completing a valid crop in the main preview or on the game triggers one OCR read
  of that frozen crop. Cancelling selection does not read anything.
- **Read again / Ctrl+Alt+G** reads the existing region from the latest available
  captured frame. Allow the preview to catch up after advancing dialogue (5 fps).
- Recognized English and timings appear in the main control window. The Chinese
  overlay still shows filler, not the recognized English or a translation.
- There is one active request and at most one pending crop. New requests cancel
  the active read and replace the pending crop. Obsolete output never replaces
  the newest result. Stopping capture, changing targets, or starting a selection
  clears the previous result and cancels work.
- No polling OCR, API calls, screenshot files, or text logging. Text stays in the
  control window for the current selection; models remain loaded until exit.

## Engine and cost

RapidOcrNet **4.2.0** runs the bundled **PP-OCRv5 mobile detector and Latin
recognizer** with CPU ONNX Runtime. This is a conventional OCR pipeline, not
PaddleOCR-VL. Two intra-operation CPU threads, one inter-operation thread,
sequential text-line recognition, and disabled idle spinning limit contention.
The orientation model is initialized by the wrapper but angle detection is off
for horizontal game dialogue. Model initialization and inference run off the UI
thread; initial model loading itself is not interruptible.

NuGet restores models during development; build/publish copy `models/v5` beside
the app. Keep that folder even with a single-file EXE. No runtime downloads.
Sources and license information: [RapidOcrNet](https://github.com/BobLd/RapidOcrNet)
(Apache-2.0), [PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR) (Apache-2.0),
[ONNX Runtime](https://github.com/microsoft/onnxruntime) (MIT), and
[SkiaSharp](https://github.com/mono/SkiaSharp) (MIT).

Small or stylized fonts, busy backgrounds, typewriter animation, and overly large
crops can reduce accuracy. English horizontal dialogue is the initial scope.
The UI distinguishes recognition time from engine total (including cold loading
and bitmap setup); these are not capture-to-translation latency measurements.
There is no guaranteed latency or game-FPS budget until Windows measurements.

## Validation

`tests/Overlay.Ocr.Tests` uses the actual bundled models on synthetic three-line
English paragraphs with light and dark backgrounds, a blank crop, and a cancelled
request. Run with `dotnet run --project tests/Overlay.Ocr.Tests -c Release`.
The normal build script runs it alongside the existing geometry/visibility tests.

On the development Mac, the initial smoke run recognized all lines exactly:
212 ms cold recognition / 1298 ms engine total, and 132 ms warm recognition /
133 ms engine total. These synthetic local results do not establish Windows game
performance. Follow the OCR section of WINDOWS-TEST.md before accepting this milestone.
