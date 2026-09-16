# Rebuild Assets/app.ico with BMP frames. VS Installer / Windows shortcuts cannot
# read PNG-compressed ICO images, so Start Menu and desktop icons stay blank.
$ErrorActionPreference = "Stop"
$src = Join-Path $PSScriptRoot "..\CastRightCatchInvManagement\Assets\app.ico"
$src = [IO.Path]::GetFullPath($src)

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

public static class IcoBmpWriter
{
    public static void Rebuild(string path)
    {
        var pngs = ExtractPngs(File.ReadAllBytes(path));
        if (pngs.Count == 0)
            throw new InvalidOperationException("No PNG frames in " + path);

        Bitmap largest = pngs[pngs.Count - 1];
        int[] sizes = { 16, 24, 32, 48, 64, 256 };
        var frames = new List<Bitmap>();
        foreach (int size in sizes)
        {
            Bitmap match = null;
            foreach (var p in pngs)
            {
                if (p.Width == size && p.Height == size)
                {
                    match = p;
                    break;
                }
            }
            frames.Add(match != null ? new Bitmap(match) : Resize(largest, size));
        }

        byte[] ico = Build(frames);
        foreach (var f in frames) f.Dispose();
        foreach (var p in pngs) p.Dispose();
        File.WriteAllBytes(path, ico);
    }

    static List<Bitmap> ExtractPngs(byte[] data)
    {
        var list = new List<Bitmap>();
        int count = BitConverter.ToUInt16(data, 4);
        for (int i = 0; i < count; i++)
        {
            int o = 6 + i * 16;
            int size = BitConverter.ToInt32(data, o + 8);
            int off = BitConverter.ToInt32(data, o + 12);
            if (off < 0 || off + size > data.Length || size < 8)
                continue;
            if (data[off] != 0x89 || data[off + 1] != 0x50)
                continue;
            using (var ms = new MemoryStream(data, off, size, false))
                list.Add(new Bitmap(ms));
        }
        return list;
    }

    static Bitmap Resize(Bitmap src, int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.DrawImage(src, 0, 0, size, size);
        }
        return bmp;
    }

    static byte[] Build(List<Bitmap> frames)
    {
        using (var ms = new MemoryStream())
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write((ushort)0);
            bw.Write((ushort)1);
            bw.Write((ushort)frames.Count);
            int dir = (int)ms.Position;
            for (int i = 0; i < frames.Count; i++)
                bw.Write(new byte[16]);

            var entries = new List<byte[]>();
            foreach (var bmp in frames)
            {
                int offset = (int)ms.Position;
                byte[] dib = EncodeDib(bmp);
                bw.Write(dib);
                int w = bmp.Width >= 256 ? 0 : bmp.Width;
                int h = bmp.Height >= 256 ? 0 : bmp.Height;
                var e = new byte[16];
                e[0] = (byte)w;
                e[1] = (byte)h;
                BitConverter.GetBytes((ushort)1).CopyTo(e, 4);
                BitConverter.GetBytes((ushort)32).CopyTo(e, 6);
                BitConverter.GetBytes(dib.Length).CopyTo(e, 8);
                BitConverter.GetBytes(offset).CopyTo(e, 12);
                entries.Add(e);
            }

            ms.Position = dir;
            foreach (var e in entries)
                bw.Write(e);
            bw.Flush();
            return ms.ToArray();
        }
    }

    static byte[] EncodeDib(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        int xorStride = w * 4;
        int andStride = ((w + 31) / 32) * 4;
        var rect = new Rectangle(0, 0, w, h);
        var bits = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(40);
                bw.Write(w);
                bw.Write(h * 2);
                bw.Write((ushort)1);
                bw.Write((ushort)32);
                bw.Write(0);
                bw.Write(xorStride * h + andStride * h);
                bw.Write(0);
                bw.Write(0);
                bw.Write(0);
                bw.Write(0);
                var row = new byte[xorStride];
                for (int y = h - 1; y >= 0; y--)
                {
                    Marshal.Copy(IntPtr.Add(bits.Scan0, y * bits.Stride), row, 0, xorStride);
                    bw.Write(row);
                }
                bw.Write(new byte[andStride * h]);
                bw.Flush();
                return ms.ToArray();
            }
        }
        finally
        {
            bmp.UnlockBits(bits);
        }
    }
}
"@ -ReferencedAssemblies System.Drawing

[IcoBmpWriter]::Rebuild($src)
$bytes = [IO.File]::ReadAllBytes($src)
$count = [BitConverter]::ToUInt16($bytes, 4)
Write-Host "Rewrote $src ($($bytes.Length) bytes, $count images)"
for ($i = 0; $i -lt $count; $i++) {
    $o = 6 + $i * 16
    $off = [BitConverter]::ToUInt32($bytes, $o + 12)
    $fmt = if ($bytes[$off] -eq 0x89) { "PNG" } else { "BMP" }
    Write-Host ("  {0}x{1} {2}" -f $bytes[$o], $bytes[$o+1], $fmt)
}
