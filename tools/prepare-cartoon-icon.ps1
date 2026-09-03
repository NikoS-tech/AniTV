param(
    [ValidateSet('anitv-3d-draft.png', 'anitv-cartoon-draft.png')]
    [string]$SourceName = 'anitv-cartoon-draft.png'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Generic;
public static class AniIconCutout {
    public static void Run(string source, string output) {
        using (var src = new Bitmap(source)) {
            int w = src.Width, h = src.Height;
            var outside = new bool[w*h];
            var queue = new Queue<int>();
            Action<int,int> visit = (x,y) => {
                if (x<0 || y<0 || x>=w || y>=h) return;
                int i=y*w+x;
                if(outside[i]) return;
                var c=src.GetPixel(x,y);
                int lo=Math.Min(c.R,Math.Min(c.G,c.B)), hi=Math.Max(c.R,Math.Max(c.G,c.B));
                if(lo<180 || hi-lo>40) return;
                outside[i]=true; queue.Enqueue(i);
            };
            for(int x=0;x<w;x++){visit(x,0);visit(x,h-1);}
            for(int y=0;y<h;y++){visit(0,y);visit(w-1,y);}
            while(queue.Count>0){int i=queue.Dequeue(),x=i%w,y=i/w;visit(x-1,y);visit(x+1,y);visit(x,y-1);visit(x,y+1);}
            using(var cut=new Bitmap(w,h,PixelFormat.Format32bppArgb)) {
                int left=w,top=h,right=0,bottom=0;
                for(int y=0;y<h;y++) for(int x=0;x<w;x++) {
                    if(outside[y*w+x]) continue;
                    var c=src.GetPixel(x,y);
                    cut.SetPixel(x,y,c);
                    left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);
                }
                int bw=right-left+1,bh=bottom-top+1;
                if(bw< w/2 || bh<h/2) throw new Exception("Unexpected cutout bounds");
                int size=(int)Math.Ceiling(Math.Max(bw,bh)/0.94);
                using(var result=new Bitmap(size,size,PixelFormat.Format32bppArgb))
                using(var g=Graphics.FromImage(result)) {
                    g.Clear(Color.Transparent);
                    g.DrawImage(cut,new Rectangle((size-bw)/2,(size-bh)/2,bw,bh),new Rectangle(left,top,bw,bh),GraphicsUnit.Pixel);
                    result.Save(output,ImageFormat.Png);
                }
                Console.WriteLine("Cutout bounds: {0},{1} {2}x{3}; canvas {4}",left,top,bw,bh,size);
            }
        }
    }
}
'@
$aniRoot = Split-Path $PSScriptRoot -Parent
[AniIconCutout]::Run((Join-Path $aniRoot "Assets\$SourceName"), (Join-Path $aniRoot 'Assets\anitv-icon.png'))
Copy-Item -LiteralPath (Join-Path $aniRoot 'Assets\anitv-icon.png') -Destination (Join-Path $aniRoot 'Assets\anitv-taskbar.png') -Force
