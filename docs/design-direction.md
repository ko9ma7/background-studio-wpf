# UI direction

- Visual thesis: a calm local editing workstation, not an AI novelty demo.
- Audience: Windows users who need product, profile, document, or short-video
  cutouts without uploading files to an external API.
- First viewport: file choice and model readiness on the left, large honest
  preview canvas on the right.
- Flow: choose source → choose one background treatment → process → inspect →
  save.
- Type: Segoe UI / Malgun Gothic, 17–22 px hierarchy, short Korean labels.
- Color: `#03C75A` action green, dark neutral text, pale gray canvas.
- States: empty source, model missing/downloading/ready, disabled process,
  progress, cancellation, actionable error, completed save.
- Accessibility: visible keyboard focus, real buttons and inputs, no
  color-only status, minimum 40 px controls, plain-language errors.
- Motion: no decorative motion; only progress state.
- Asset provenance: no bundled stock image or generated fake UI.
- Verification: Release build, unit tests, model download, image processing,
  100/125/150% Windows scaling, keyboard flow, and missing-FFmpeg state.

