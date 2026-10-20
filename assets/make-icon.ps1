# make-icon.ps1 - 从方形母版图重新生成 src\app.ico
# 用法:  .\assets\make-icon.ps1  （使用 assets\icon.png 作为母版）
#       .\assets\make-icon.ps1 -SourceImage "任意方形母版.png"
# 依赖: Windows 自带的 System.Drawing (GDI+), 无需安装任何组件
#
# 流程: 全出血方形母版 -> 圆角透明遮罩(应用图标标准形状) -> 256/64/48/32/16 多尺寸
#      -> 打包为 PNG-in-ICO (Win10/11 标准方案, 与 Electron/Chrome 同路线)

param(
    [string]$SourceImage = (Join-Path $PSScriptRoot "icon.png"),
    [string]$OutIco = (Join-Path $PSScriptRoot "..\src\app.ico"),
    [double]$RadiusRatio = 0.225
)

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

public static class IconBuilder
{
    // 圆角遮罩: 应用图标标准圆角形状, 四角区域转为透明
    public static Bitmap RoundCorners(Bitmap src, double radiusRatio)
    {
        int size = src.Width;
        int radius = (int)Math.Round(size * radiusRatio);
        Bitmap dst = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Bitmap mask = new Bitmap(size, size, PixelFormat.Format32bppArgb))
        using (Graphics mg = Graphics.FromImage(mask))
        {
            mg.SmoothingMode = SmoothingMode.AntiAlias;
            mg.Clear(Color.White);
            int d = 2 * radius;
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddArc(0, 0, d, d, 180, 90);
                p.AddArc(size - d, 0, d, d, 270, 90);
                p.AddArc(size - d, size - d, d, d, 0, 90);
                p.AddArc(0, size - d, d, d, 90, 90);
                p.CloseFigure();
                using (SolidBrush b = new SolidBrush(Color.Black)) mg.FillPath(b, p);
            }
            Rectangle rect = new Rectangle(0, 0, size, size);
            BitmapData sd = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData md = mask.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData dd = dst.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            int bytes = size * size * 4;
            byte[] sBuf = new byte[bytes];
            byte[] mBuf = new byte[bytes];
            byte[] dBuf = new byte[bytes];
            Marshal.Copy(sd.Scan0, sBuf, 0, bytes);
            Marshal.Copy(md.Scan0, mBuf, 0, bytes);
            for (int i = 0; i < bytes; i += 4)
            {
                int keep = 255 - mBuf[i];
                dBuf[i]     = sBuf[i];
                dBuf[i + 1] = sBuf[i + 1];
                dBuf[i + 2] = sBuf[i + 2];
                dBuf[i + 3] = (byte)((int)sBuf[i + 3] * keep / 255);
            }
            Marshal.Copy(dBuf, 0, dd.Scan0, bytes);
            src.UnlockBits(sd);
            mask.UnlockBits(md);
            dst.UnlockBits(dd);
        }
        return dst;
    }

    public static byte[] ResizeToPng(Bitmap src, int target)
    {
        using (Bitmap b = new Bitmap(target, target, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(b))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.DrawImage(src, 0, 0, target, target);
            using (MemoryStream ms = new MemoryStream())
            {
                b.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }
    }

    // 打包多尺寸 PNG-in-ICO
    public static void BuildIco(Bitmap master, string icoPath, int[] sizes)
    {
        byte[][] blobs = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++) blobs[i] = ResizeToPng(master, sizes[i]);
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter bw = new BinaryWriter(ms))
        {
            bw.Write((ushort)0);
            bw.Write((ushort)1);
            bw.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                byte dim = s >= 256 ? (byte)0 : (byte)s;
                bw.Write(dim); bw.Write(dim);
                bw.Write((byte)0); bw.Write((byte)0);
                bw.Write((ushort)1);
                bw.Write((ushort)32);
                bw.Write((uint)blobs[i].Length);
                bw.Write((uint)offset);
                offset += blobs[i].Length;
            }
            for (int i = 0; i < sizes.Length; i++) bw.Write(blobs[i]);
            bw.Flush();
            File.WriteAllBytes(icoPath, ms.ToArray());
        }
    }
}
"@

$srcPath = (Resolve-Path $SourceImage).Path
$src = New-Object System.Drawing.Bitmap($srcPath)
if ($src.Width -ne $src.Height) { throw "母版图必须为正方形: $srcPath ($($src.Width)x$($src.Height))" }
Write-Host "源图: $srcPath ($($src.Width)x$($src.Height))"

$master = [IconBuilder]::RoundCorners($src, $RadiusRatio)
$src.Dispose()

$outPath = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutIco))
[IconBuilder]::BuildIco($master, $outPath, @(256, 64, 48, 32, 16))
$master.Dispose()

Write-Host "ICO 已生成: $outPath"
Write-Host ("大小: {0:N1} KB" -f ((Get-Item $outPath).Length / 1KB))
