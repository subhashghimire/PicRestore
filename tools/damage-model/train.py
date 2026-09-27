"""Train the built-in damage detector on one or more prepared pairs.

    python train.py pairs/photo1 [pairs/photo2 ...] [--cv]

Writes detector_model.json (weights for gen_model_cs.py) and BuiltInReplay.bin (a compact sample of
per-pixel features + labels that the app mixes into in-app training so the model doesn't forget what it
learned here, and uses to check new models haven't regressed).

--cv also reports, per pair, the score on held-out blocks (the same 25% block split the app uses) and,
with 2+ pairs, leave-one-photo-out scores - the honest measure of how well it generalises.

Sampling matches the app's trainer (DamageModelTrainer.cs): the same number of pixels from every pair,
with damage boosted to at least 30% of each pair's samples, so a heavily damaged photo can't drown out
lightly damaged ones.
"""
import sys, json, struct, warnings, numpy as np
warnings.filterwarnings("ignore")
from sklearn.neural_network import MLPClassifier
from sklearn.preprocessing import StandardScaler
from scipy.ndimage import uniform_filter
import features as F

F.RADII = (2, 6, 16, 40); HIDDEN = (32, 16); SMOOTH = 1; THRESHOLD = 0.5
SAMPLES_PER_PAIR = 60000; POSITIVE_FLOOR = 0.30; REPLAY_PER_PAIR = 10000
rng = np.random.default_rng(0)

def held_out(h, w):
    """Same split as DamageModelTrainer.HeldOutBlocks: 4x4 blocks, in block-row r the column (3r+1) mod 4."""
    by = (np.arange(h) * 4 // h)[:, None]; bx = (np.arange(w) * 4 // w)[None, :]
    return (bx == (by * 3 + 1) % 4).ravel()

def load(d):
    z = np.load(f"{d}/data.npz"); X, names = F.compute(z["work"]); h, w = z["gt"].shape
    return dict(name=d.rstrip("/").split("/")[-1], X=X, y=z["gt"].ravel(), k=z["known"].ravel(), h=h, w=w, held=held_out(h, w), names=names)

def sample(p, allowed, n):
    cand = np.flatnonzero(allowed); y = p["y"][cand]
    pos, neg = cand[y], cand[~y]
    n = min(n, len(cand)); npos = min(len(pos), int(n * max(POSITIVE_FLOOR, y.mean() if len(y) else 0)))
    nneg = min(len(neg), n - npos)
    return np.concatenate([rng.choice(pos, npos, replace=False), rng.choice(neg, nneg, replace=False)])

def fit(parts):
    X = np.concatenate([p["X"][s] for p, s in parts]); y = np.concatenate([p["y"][s] for p, s in parts])
    sc = StandardScaler().fit(X)
    clf = MLPClassifier(HIDDEN, max_iter=120, early_stopping=True, random_state=0, alpha=1e-4).fit(sc.transform(X), y)
    return sc, clf

def prob(sc, clf, p):
    raw = clf.predict_proba(sc.transform(p["X"]))[:, 1].reshape(p["h"], p["w"])
    return (uniform_filter(raw, 2 * SMOOTH + 1, mode="nearest") if SMOOTH else raw).ravel()

def score(pm, p, sel):
    pr = pm[sel] > THRESHOLD; g = p["y"][sel]; tp = (pr & g).sum()
    return tp / max((pr | g).sum(), 1), tp / max(pr.sum(), 1), tp / max(g.sum(), 1), pr.mean()

args = [a for a in sys.argv[1:] if not a.startswith("--")]; cv = "--cv" in sys.argv
pairs = [load(d) for d in args]
if cv:
    sc, clf = fit([(p, sample(p, p["k"] & ~p["held"], SAMPLES_PER_PAIR)) for p in pairs])
    for p in pairs:
        iou, pr, rc, fl = score(prob(sc, clf, p), p, p["k"] & p["held"])
        print(f"{p['name']:18s} held-out blocks: IoU {iou:.3f} precision {pr:.2f} recall {rc:.2f} (flags {fl*100:.1f}% of those pixels, truth {p['y'][p['k'] & p['held']].mean()*100:.1f}%)", flush=True)
    if len(pairs) > 1:
        for i, p in enumerate(pairs):
            sc, clf = fit([(q, sample(q, q["k"], SAMPLES_PER_PAIR)) for j, q in enumerate(pairs) if j != i])
            iou, pr, rc, fl = score(prob(sc, clf, p), p, p["k"])
            print(f"{p['name']:18s} leave-this-photo-out: IoU {iou:.3f} precision {pr:.2f} recall {rc:.2f} (flags {fl*100:.1f}%, truth {p['y'][p['k']].mean()*100:.1f}%)", flush=True)

sc, clf = fit([(p, sample(p, p["k"], SAMPLES_PER_PAIR)) for p in pairs])
for p in pairs:
    iou, pr, rc, fl = score(prob(sc, clf, p), p, p["k"])
    print(f"{p['name']:18s} shipped model (in-sample): IoU {iou:.3f} precision {pr:.2f} recall {rc:.2f} (flags {fl*100:.1f}%)")
json.dump({"names": pairs[0]["names"], "mean": sc.mean_.tolist(), "scale": sc.scale_.tolist(),
           "W": [c.tolist() for c in clf.coefs_], "b": [b.tolist() for b in clf.intercepts_],
           "radii": list(F.RADII), "smooth": SMOOTH, "threshold": THRESHOLD, "pairs": [p["name"] for p in pairs]},
          open("detector_model.json", "w"))

# Replay set: raw features (not standardised) as float16, labels, weights (1.0), balanced like training.
rows = [(p, sample(p, p["k"], REPLAY_PER_PAIR)) for p in pairs]
X = np.concatenate([p["X"][s] for p, s in rows]).astype(np.float16); y = np.concatenate([p["y"][s] for p, s in rows]).astype(np.uint8)
perm = rng.permutation(len(y)); X, y = X[perm], y[perm]
with open("BuiltInReplay.bin", "wb") as f:
    f.write(struct.pack("<iiiii", 0x53524950, 1, len(y), X.shape[1], len(pairs)))
    f.write(X.tobytes()); f.write(y.tobytes()); f.write(np.ones(len(y), np.float16).tobytes())
print(f"wrote detector_model.json and BuiltInReplay.bin ({len(y)} rows) - now run gen_model_cs.py and copy BuiltInReplay.bin to src/PicRestore.Restoration/DamageDetection/")
