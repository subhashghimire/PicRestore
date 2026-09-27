# faithful-photo-restoration

Restores old, faded, or damaged vintage photographs (e.g., Polaroids) using conservative archival principles. Use when requested to restore, clean, or repair photographs while strictly preserving original faces, clothing patterns, designs, and intact details without AI reinterpretation.

## Instructions

### Faithful Photo Restoration

Archival restoration protocol for vintage and Polaroid photographs. Prioritizes historical accuracy, identity preservation, and conservative localized repair over generative reinterpretation.

### Restoration Principles

**1. Ground Truth & Strict Preservation**

- Intact Areas as Ground Truth: Never modify, redraw, beautify, or replace undamaged regions simply to make them sharper or cleaner.
- Clothing Patterns & Fabrics: Retain original clothing patterns, diagonal stripes, pinstripes, prints, and fabric layouts. Never allow generative models to replace simple or degraded patterns with hallucinated ornate/floral motifs.
- Faces & Identity: Do not alter facial structure, eyes, nose, mouth, hair, expressions, or body proportions. Avoid beauty filters or synthetic face replacement.

**2. Localized Damage Repair**

- Scan for specific white patches, chemical stains, fading, scratches, and spots.
- Reconstruct only genuinely damaged areas using surrounding contextual pixels, edges, lighting, and textures.
- Prefer conservative pixel repair / localized inpainting over full-frame generative re-synthesis.

**3. Color & Lighting Restoration**

- Correct aging color casts (yellowing, whitening, bleaching, color shifts) using surviving intact regions as the primary reference.
- Recover natural saturation and skin tones without introducing modern, overly saturated digital color palettes.
- Retain period-accurate film and Polaroid color characteristics.

**4. Controlled Sharpening**

- Apply slight global sharpening (~10%) solely to compensate for age-related lens blur and chemical fading.
- Avoid hyper-sharpness, plastic skin textures, modern HDR effects, or artificial micro-details.

### Execution Checklist

1. Damage Masking: Isolate damaged regions (e.g., corner stains, chemical spots) for targeted repair.
2. Locked Elements: Lock intact facial features and clothing patterns to prevent unwanted pattern replacement.
3. Tonal Correction: Adjust color cast and contrast conservatively.
4. Subtle Sharpening: Apply 10% sharpening pass for final clarity.

---

## How PicRestore implements it

| Protocol item | Where in the code | Behaviour |
| --- | --- | --- |
| Damage masking | `LearnedDamageDetector`, Mask Editor | A learned detector proposes the mask; the user reviews it with the damage and protect brushes before anything changes. The detector can keep learning from before/after pairs (Settings). |
| Locked elements: intact pixels | `LamaInpainter.LockedBelowProbability` | Mask growth and seam blending never reach pixels the detector is confident are intact, nor protect-brushed pixels. |
| Locked elements: faces | `YuNetFaceDetector`, `FaceLock` | Faces are detected automatically. Inside a face, only damage flagged with ≥ 85% confidence (or painted by hand) is repaired. |
| Localized repair | `LamaInpainter` | Only masked pixels are rebuilt, by LaMa, from the surrounding context. There is no full-frame re-synthesis and no face replacement. AI face enhancement is opt-in and off. |
| Tonal correction | `HistogramColorRestorer` | Statistics come from intact areas only. Each correction fires only when needed: white balance for casts over 12%, a contrast stretch only if the range is compressed, gamma only for washed-out prints, and saturation only for faded prints (capped at +20%). |
| Subtle sharpening | `UnsharpMaskSharpener` | Final luminance-only unsharp mask at 10% (Settings), with a noise threshold and a per-pixel cap. Protected pixels are skipped. |

Known limit: content that is completely destroyed (e.g. a face hidden under a blotch) is filled plausibly from its surroundings. It is not re-drawn. Tools that re-draw it are generating new detail, which this protocol rules out.
