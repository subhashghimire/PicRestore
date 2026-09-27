# Damage-detector training tools

`LearnedDamageDetector` is a small neural network (34 features -> 32 -> 16 -> 1) whose weights live in
`src/PicRestore.Restoration/DamageDetection/LearnedDamageModel.g.cs`. These scripts produce those
weights from **before/after pairs**: a damaged scan plus a restored version of the same photo (from a
professional restorer, another tool, or your own careful manual retouch). Where the two differ after
alignment and a global colour match is treated as damage.

```bash
pip install -r requirements.txt

# 1. one folder of training data per pair (aligns, colour-matches, builds the damage map + a preview)
python make_pair.py damaged.jpg restored.jpg pairs/polaroid-terrace
python make_pair.py another_damaged.jpg another_restored.jpg pairs/another-photo

# 2. train on every pair (add --cv for held-out and leave-one-photo-out scores)
python train.py pairs/* --cv

# 3. regenerate the C# weights and the replay set, then rebuild the app
python gen_model_cs.py detector_model.json ../../src/PicRestore.Restoration/DamageDetection/LearnedDamageModel.g.cs
cp BuiltInReplay.bin ../../src/PicRestore.Restoration/DamageDetection/
```

Check `pairs/<name>/ground_truth_preview.png` before training: red should cover the damage and little
else. A reference that re-imagined large parts of the scene (new faces, new objects) will mark those as
"damage" too, which is usually still what you want for the detector.

`features.py` and `resample.py` must stay in step with `DamageFeatures.cs` / `ImageOps.cs`; the C#
detector reproduces the Python probabilities to within 2e-5 on the reference photo.

This is for the built-in model. End users don't need Python: the app's Settings page runs the same
steps (alignment, damage map, training, held-out check) on-device and keeps improving from every pair
they add.

Current built-in model: 5 pairs (polaroid-terrace, baby-tika, field-hoeing, family-group, garden-kiss).

| Pair | Held-out IoU | Leave-this-photo-out IoU |
| --- | --- | --- |
| polaroid-terrace (heavy silvering) | 0.52 | 0.04 |
| baby-tika (dust specks) | 0.19 | 0.04 |
| field-hoeing (specks, one stain) | 0.02 | 0.02 |
| family-group (dust specks) | 0.11 | 0.04 |
| garden-kiss (silver flakes) | 0.12 | 0.09 |

The low leave-one-photo-out numbers mean it does not yet generalise well to unseen photos. More pairs
(especially of the kinds of damage you actually have) is the way to improve that.
