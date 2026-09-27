"""Exact area (box) resampling, separable. Mirrors AreaResampler.cs line for line."""
import numpy as np

def weights(n_in, n_out):
    """Row i: weights over input pixels covering output pixel i. Returns dense (n_out, n_in)."""
    W = np.zeros((n_out, n_in), np.float64)
    scale = n_in / n_out
    for i in range(n_out):
        start, end = i * scale, (i + 1) * scale
        j0, j1 = int(np.floor(start)), min(int(np.ceil(end)), n_in)
        for j in range(j0, j1):
            ov = min(end, j + 1) - max(start, j)
            if ov > 0: W[i, j] = ov
        W[i] /= W[i].sum()
    return W

def area_resize(a, out_w, out_h):
    """a: HxW or HxWxC float32; downsampling only (area average)."""
    H, W = a.shape[:2]
    Wy, Wx = weights(H, out_h), weights(W, out_w)
    if a.ndim == 2:
        return (Wy @ a.astype(np.float64) @ Wx.T).astype(np.float32)
    return np.stack([(Wy @ a[..., c].astype(np.float64) @ Wx.T) for c in range(a.shape[2])], -1).astype(np.float32)

def working_size(w, h, long_side=1280):
    s = min(1.0, long_side / max(w, h))
    return max(1, int(round(w * s))), max(1, int(round(h * s)))
