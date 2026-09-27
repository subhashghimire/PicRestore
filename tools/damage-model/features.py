"""Generic per-pixel damage features. Everything here is box-filter based so the C# port can use the
existing summed-area-table LocalStatistics class (same maths, same boundary handling)."""
import numpy as np
from scipy.ndimage import uniform_filter

RADII = (2, 6, 16, 40)

def box(a, r):
    return uniform_filter(a, size=2*r+1, mode="nearest")

def compute(rgb):
    """rgb: HxWx3 float32 in [0,1]. Returns (H*W, F) float32 and feature names."""
    R, G, B = rgb[...,0], rgb[...,1], rgb[...,2]
    L = 0.2126*R + 0.7152*G + 0.0722*B
    mx = rgb.max(2); mn = rgb.min(2)
    S = np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0)
    O1 = R - G                      # red-green opponent
    O2 = (R + G) / 2 - B            # yellow-blue opponent
    # Sobel-free gradient: central differences (cheap, easy to mirror exactly in C#)
    gx = np.zeros_like(L); gy = np.zeros_like(L)
    gx[:,1:-1] = (L[:,2:] - L[:,:-2]) / 2; gy[1:-1] = (L[2:] - L[:-2]) / 2
    Gm = np.sqrt(gx*gx + gy*gy)
    feats, names = [], []
    def add(a, n): feats.append(a.astype(np.float32)); names.append(n)
    for a, n in [(L,"L"), (S,"S"), (O1,"O1"), (O2,"O2"), (mx,"Max"), (mn,"Min")]:
        add(a, n)
    for r in RADII:
        mL = box(L, r); vL = np.maximum(box(L*L, r) - mL*mL, 0)
        add(L - mL, f"L-mean{r}"); add(np.sqrt(vL), f"Lstd{r}")
        add(S - box(S, r), f"S-mean{r}")
        add(O1 - box(O1, r), f"O1-mean{r}"); add(O2 - box(O2, r), f"O2-mean{r}")
        add(box(Gm, r), f"Grad{r}")
    for r in (6, 40):
        add(box(S, r), f"Smean{r}"); add(box(L, r), f"Lmean{r}")
    return np.stack([f.ravel() for f in feats], 1), names
