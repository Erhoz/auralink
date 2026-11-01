using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

internal static class BuildIcon
{
    // Faithful GDI+ transcription of assets/AuraLink.svg; no external rasterizer.
    private static Bitmap Render(int size)
    {
        using(var large=new Bitmap(size*4,size*4,PixelFormat.Format32bppArgb))
        {
            using(var g=Graphics.FromImage(large))
            {
                g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;g.ScaleTransform(size*4/100f,size*4/100f);
                using(var tile=new GraphicsPath())
                using(var fill=new SolidBrush(Color.FromArgb(12,14,20)))
                {tile.AddArc(0,0,40,40,180,90);tile.AddArc(60,0,40,40,270,90);tile.AddArc(60,60,40,40,0,90);tile.AddArc(0,60,40,40,90,90);tile.CloseFigure();g.FillPath(fill,tile);}
                using(var white=new Pen(Color.White,16){StartCap=LineCap.Round,EndCap=LineCap.Round})g.DrawLine(white,35,35,65,65);
                using(var path=new GraphicsPath())
                using(var pen=new Pen(Color.FromArgb(139,92,246),14){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})
                {
                    path.AddLine(22,45,45,22);path.AddBezier(45,22,49,18,56,18,60,22);path.AddLine(60,22,78,40);path.AddBezier(78,40,82,44,82,51,78,55);
                    path.AddLine(78,55,55,78);path.AddBezier(55,78,51,82,44,82,40,78);path.AddLine(40,78,22,60);path.AddBezier(22,60,18,56,18,49,22,45);path.CloseFigure();g.DrawPath(pen,path);
                }
                using(var path=new GraphicsPath())
                using(var pen=new Pen(Color.FromArgb(6,182,212),14){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})
                {path.AddLine(45,22,22,45);path.AddBezier(22,45,18,49,18,56,22,60);path.AddLine(22,60,40,78);path.AddBezier(40,78,44,82,51,82,55,78);path.AddLine(55,78,73,60);g.DrawPath(pen,path);}
            }
            var result=new Bitmap(size,size,PixelFormat.Format32bppArgb);
            using(var g=Graphics.FromImage(result)){g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;g.DrawImage(large,new Rectangle(0,0,size,size));}
            return result;
        }
    }
    private static void Main(string[] args)
    {
        string directory=args[0];Directory.CreateDirectory(directory);
        int[] sizes={16,20,24,32,40,48,64,128,256};var data=new List<byte[]>();
        foreach(int size in sizes)using(var bitmap=Render(size))using(var stream=new MemoryStream()){bitmap.Save(stream,ImageFormat.Png);data.Add(stream.ToArray());}
        using(var file=new BinaryWriter(File.Create(Path.Combine(directory,"AuraLink.ico"))))
        {
            file.Write((ushort)0);file.Write((ushort)1);file.Write((ushort)sizes.Length);int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++){file.Write((byte)(sizes[i]==256 ? 0 : sizes[i]));file.Write((byte)(sizes[i]==256 ? 0 : sizes[i]));file.Write((byte)0);file.Write((byte)0);file.Write((ushort)1);file.Write((ushort)32);file.Write(data[i].Length);file.Write(offset);offset+=data[i].Length;}
            foreach(var bytes in data)file.Write(bytes);
        }
        using(var preview=new Bitmap(600,270))
        using(var g=Graphics.FromImage(preview))
        using(var font=new Font("Segoe UI",11))
        {
            g.Clear(Color.FromArgb(238,241,246));g.FillRectangle(Brushes.Black,0,135,600,135);
            for(int row=0;row<2;row++)
            {int x=20;foreach(int size in new[]{16,24,32,48,64,96}){using(var bitmap=Render(size))g.DrawImageUnscaled(bitmap,x,20+row*135);g.DrawString(size+"px",font,row==0 ? Brushes.Black : Brushes.White,x,112+row*135);x+=size+25;}}
            preview.Save(Path.Combine(directory,"icon-preview.png"),ImageFormat.Png);
        }
    }
}
