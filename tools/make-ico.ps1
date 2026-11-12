# Launchly logosundan uygulama ikonunu üretir (tekrar çalıştırılabilir).
#   Girdi : assets\logo-source.jpg (kare, en az 1024 px; köşeleri saydam değil)
#   Çıktı : src\Launchly.App\Assets\Launchly.ico (16, 20, 24, 32, 40, 48, 64, 256 px; her giriş PNG kodlu)
#           src\Launchly.App\Assets\Launchly.png (512 px, saydam köşeli; Hakkında sayfası)
# Kaynak görselin yuvarlak köşe dışı (beyaz zemin) geometrik maskeyle saydam kırpılır.
# Kullanım: powershell -NoProfile -ExecutionPolicy Bypass -File tools\make-ico.ps1
param(
    [string]$Source = (Join-Path (Split-Path $PSScriptRoot -Parent) 'assets\logo-source.jpg'),
    [string]$OutDir = (Join-Path (Split-Path $PSScriptRoot -Parent) 'src\Launchly.App\Assets'),
    # Yuvarlak köşe yarıçapı, kaynak genişliğine oranla (1024 px'te ~200 px ölçüldü).
    [double]$CornerRatio = 0.1953,
    [int]$PngSize = 512
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not ('LaunchlyIconTools' -as [type])) {
    Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class LaunchlyIconTools
{
    // Yuvarlak dikdörtgen maskesi: köşe dışı saydam, kenar kenar yumuşatılmış (alfa = içeri uzaklık + 0,5).
    // Kenar bandındaki JPG beyaz saçağı zemin rengiyle değiştirilir.
    public static Bitmap Mask(Bitmap src, double radius)
    {
        int w = src.Width, h = src.Height;
        var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        {
            g.DrawImage(src, new Rectangle(0, 0, w, h));
        }

        var data = dst.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var px = new byte[data.Stride * h];
        Marshal.Copy(data.Scan0, px, 0, px.Length);

        // Zemin rengi: üst kenarın ortasından, saçaktan uzak bir nokta.
        int bgOff = (int)(radius * 0.5) * data.Stride + (w / 2) * 4;
        byte bb = px[bgOff], bg = px[bgOff + 1], br = px[bgOff + 2];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                double cx = x + 0.5, cy = y + 0.5;
                // Kenara içeriden uzaklık (yuvarlak köşelerde çembere göre).
                double qx = Math.Max(radius - cx, cx - (w - radius));
                double qy = Math.Max(radius - cy, cy - (h - radius));
                double inside;
                if (qx > 0 && qy > 0)
                {
                    inside = radius - Math.Sqrt(qx * qx + qy * qy);
                }
                else
                {
                    inside = Math.Min(Math.Min(cx, w - cx), Math.Min(cy, h - cy));
                }

                int o = y * data.Stride + x * 4;
                if (inside >= 4)
                {
                    continue;
                }

                double a = Math.Max(0, Math.Min(1, inside + 0.5));
                px[o] = bb; px[o + 1] = bg; px[o + 2] = br;
                px[o + 3] = (byte)Math.Round(a * 255);
            }
        }

        Marshal.Copy(px, 0, data.Scan0, px.Length);
        dst.UnlockBits(data);
        return dst;
    }

    // Kademeli yarıya indirme + HighQualityBicubic: büyük oranlı küçültmede ayrıntı kaybını azaltır.
    public static Bitmap Resize(Bitmap src, int size)
    {
        Bitmap current = src;
        while (current.Width / 2 >= size * 2)
        {
            var half = Draw(current, current.Width / 2);
            if (!ReferenceEquals(current, src)) current.Dispose();
            current = half;
        }

        var result = Draw(current, size);
        if (!ReferenceEquals(current, src)) current.Dispose();
        return result;
    }

    private static Bitmap Draw(Bitmap src, int size)
    {
        var dst = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        using (var attr = new ImageAttributes())
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            attr.SetWrapMode(WrapMode.TileFlipXY);   // kenarda koyu/saydam hale olmasın
            g.DrawImage(src, new Rectangle(0, 0, size, size), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attr);
        }

        return dst;
    }
}
'@
}

if (-not (Test-Path $Source)) { throw "Kaynak logo bulunamadı: $Source" }
New-Item -ItemType Directory -Force $OutDir | Out-Null

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$src = New-Object System.Drawing.Bitmap $Source
try {
    if ($src.Width -ne $src.Height) { throw "Kaynak kare değil: $($src.Width)x$($src.Height)" }
    $masked = [LaunchlyIconTools]::Mask($src, $src.Width * $CornerRatio)
} finally {
    $src.Dispose()
}

try {
    # PNG (Hakkında sayfası)
    $png = [LaunchlyIconTools]::Resize($masked, $PngSize)
    $png.Save((Join-Path $OutDir 'Launchly.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    $png.Dispose()

    # ICO: ICONDIR + ICONDIRENTRY[] + PNG verileri
    $images = foreach ($s in $sizes) {
        $bmp = [LaunchlyIconTools]::Resize($masked, $s)
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        , $ms.ToArray()
    }

    $out = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $out
    $w.Write([UInt16]0)               # ayrılmış
    $w.Write([UInt16]1)               # tür: ikon
    $w.Write([UInt16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]
        $dim = if ($s -ge 256) { 0 } else { $s }   # 256 px başlıkta 0 yazılır
        $w.Write([byte]$dim)              # genişlik
        $w.Write([byte]$dim)              # yükseklik
        $w.Write([byte]0)                 # palet
        $w.Write([byte]0)                 # ayrılmış
        $w.Write([UInt16]1)               # düzlem
        $w.Write([UInt16]32)              # bit derinliği
        $w.Write([UInt32]$images[$i].Length)
        $w.Write([UInt32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($img in $images) { $w.Write([byte[]]$img) }
    $w.Flush()
    [System.IO.File]::WriteAllBytes((Join-Path $OutDir 'Launchly.ico'), $out.ToArray())
    $w.Dispose()

    Write-Host "Üretildi: $(Join-Path $OutDir 'Launchly.ico') ($($sizes -join ', ') px), $(Join-Path $OutDir 'Launchly.png') ($PngSize px)"
} finally {
    $masked.Dispose()
}
