"""Turn one (damaged scan, restored reference) pair into detector training data.

    python make_pair.py damaged.jpg restored.jpg pairs/<name>

Steps (all generic - nothing about a particular photo is hard-coded):
 1. Align the reference to the damaged scan (SIFT + RANSAC homography; restoration tools often crop or
    rescale slightly - misalignment alone would otherwise look like "damage" everywhere).
 2. Fit a global colour mapping damaged -> reference (2nd-order polynomial, robust to outliers), so the
    reference's overall colour grading isn't mistaken for damage.
 3. Ground truth "damage" = pixels where the colour-mapped scan still differs from the reference by more
    than a threshold, cleaned with a small morphological open/close.
 4. Save the working-resolution image, the ground-truth mask and a "known" mask (where the aligned
    reference exists) as data.npz, plus a preview overlay.
"""
import sys, os, numpy as np, cv2
from scipy.ndimage import gaussian_filter, binary_opening, binary_closing, uniform_filter
from resample import area_resize, working_size

RESIDUAL_THRESHOLD = 0.10
TEXTURE_FACTOR = 1.5

def main(damaged_path, restored_path, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    dam = cv2.imread(damaged_path, cv2.IMREAD_COLOR)[..., ::-1].astype(np.float32) / 255
    ref = cv2.imread(restored_path, cv2.IMREAD_COLOR)[..., ::-1].astype(np.float32) / 255
    H, W = dam.shape[:2]
    # 1) align at the reference's scale for robust matching, then map into the damaged frame
    rh, rw = ref.shape[:2]
    dam_s = cv2.resize(dam, (rw, rh), interpolation=cv2.INTER_AREA)
    g1 = cv2.cvtColor((dam_s*255).astype(np.uint8), cv2.COLOR_RGB2GRAY); g2 = cv2.cvtColor((ref*255).astype(np.uint8), cv2.COLOR_RGB2GRAY)
    sift = cv2.SIFT_create(8000)
    k1, d1 = sift.detectAndCompute(g1, None); k2, d2 = sift.detectAndCompute(g2, None)
    good = [a for a, b in cv2.BFMatcher().knnMatch(d1, d2, k=2) if a.distance < 0.7 * b.distance]
    src = np.float32([k1[g.queryIdx].pt for g in good]); dst = np.float32([k2[g.trainIdx].pt for g in good])
    Hm, inl = cv2.findHomography(dst, src, cv2.RANSAC, 4.0)
    print(f"alignment: {len(good)} matches, {int(inl.sum())} inliers")
    S = np.diag([W / rw, H / rh, 1.0])
    Hfull = S @ Hm  # ref(rw,rh) -> dam_s(rw,rh) -> dam(W,H)
    ref_full = cv2.warpPerspective(ref, Hfull, (W, H), flags=cv2.INTER_CUBIC)
    known = cv2.warpPerspective(np.ones((rh, rw), np.float32), Hfull, (W, H), flags=cv2.INTER_NEAREST) > 0.5
    known = cv2.erode(known.astype(np.uint8), np.ones((9, 9), np.uint8)) > 0
    # 2) global colour mapping on known pixels
    def feats(a):
        r, g, b = a[..., 0].ravel(), a[..., 1].ravel(), a[..., 2].ravel()
        return np.stack([np.ones_like(r), r, g, b, r*r, g*g, b*b, r*g, r*b, g*b], 1)
    idx = np.flatnonzero(known.ravel()); idx = np.random.default_rng(0).choice(idx, min(300000, len(idx)), replace=False)
    X = feats(dam)[idx]; Y = ref_full.reshape(-1, 3)[idx]; w = np.ones(len(idx), bool)
    for _ in range(3):
        coef, *_ = np.linalg.lstsq(X[w], Y[w], rcond=None)
        res = np.abs(X @ coef - Y).mean(1); w = res < np.quantile(res, 0.6)
    mapped = np.clip((feats(dam) @ coef).reshape(dam.shape), 0, 1)
    # 3) residual at twice the working resolution (fine enough for thin scratches, coarse enough to
    #    ignore JPEG noise and sub-pixel misalignment) -> cleaned mask -> working resolution
    ww, wh = working_size(W, H)
    fw, fh = working_size(W, H, 2 * max(ww, wh))
    m_f = area_resize(mapped, fw, fh); r_f = area_resize(ref_full, fw, fh)
    resid = np.abs(gaussian_filter(m_f, (1.5, 1.5, 0)) - gaussian_filter(r_f, (1.5, 1.5, 0))).mean(2)
    # Restoration tools that re-synthesise texture (foliage, soil, fabric) differ from the scan there even
    # where nothing was damaged. Require the residual to stand out against the reference's own local
    # texture: in flat areas the plain threshold applies, in busy areas the bar rises with the texture.
    ref_l = r_f @ np.array([0.2126, 0.7152, 0.0722], np.float32)
    mean = uniform_filter(ref_l, 7); texture = np.sqrt(np.maximum(uniform_filter(ref_l * ref_l, 7) - mean * mean, 0))
    gt_f = resid > np.maximum(RESIDUAL_THRESHOLD, TEXTURE_FACTOR * texture)
    gt_f = binary_closing(binary_opening(gt_f, iterations=1), iterations=2)
    gt = area_resize(gt_f.astype(np.float32), ww, wh) > 0.5
    known_w = area_resize(known.astype(np.float32), ww, wh) > 0.999
    work = area_resize(dam, ww, wh)
    np.savez_compressed(os.path.join(out_dir, "data.npz"), work=work, gt=gt, known=known_w)
    vis = (work * 255).astype(np.uint8).copy(); sel = gt & known_w
    vis[sel] = (0.4 * vis[sel] + 0.6 * np.array([255, 0, 0])).astype(np.uint8)
    cv2.imwrite(os.path.join(out_dir, "ground_truth_preview.png"), vis[..., ::-1])
    print(f"{out_dir}: working {ww}x{wh}, damage share {gt[known_w].mean()*100:.1f}% of known area")

if __name__ == "__main__":
    main(*sys.argv[1:4])
