using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.Framework.WpfInterop.Input;
using ACViewer.Config;
using ACViewer.Enum;
using ACE.DatLoader;
using ACE.DatLoader.FileTypes;

namespace ACViewer
{
    public class TextureGalleryViewer
    {
        public static TextureGalleryViewer Instance { get; private set; }
        private readonly List<uint> _textureIds = new();
        private readonly Dictionary<uint, Texture2D> _cache = new();
        private readonly Queue<uint> _loadQueue = new();
        private const double LoadBudgetMs = 6.0;
        private const int MaxCached = 512;
        private int _thumbSize = 96;
        private int _cols;
        private int _spacing = 8;
        private int _scroll;
        private int _maxScroll;
        private string _filter = string.Empty;
        private SpriteFont _font => GameView.Instance.Font;
        private WpfKeyboard Keyboard => GameView.Instance._keyboard;
        private WpfMouse Mouse => GameView.Instance._mouse;
        private KeyboardState PrevKeyboard => GameView.Instance.PrevKeyboardState;
        private MouseState PrevMouse => GameView.Instance.PrevMouseState;

        public TextureGalleryViewer(){ Instance=this; BuildIdList(); }
        private void BuildIdList(){ _textureIds.Clear(); if (DatManager.PortalDat==null) return; foreach(var id in DatManager.PortalDat.AllFiles.Keys){ var t=id>>24; if(t==0x06||t==0x05||t==0x04) _textureIds.Add(id);} _textureIds.Sort(); }
        public void SetFilter(string filter){ _filter=filter?.Trim().ToLowerInvariant()??string.Empty; _scroll=0; }
        public void Update(GameTime time){ HandleInput(); PumpLoader(); }
        private void HandleInput(){ var ks=Keyboard.GetState(); var ms=Mouse.GetState(); if(ms.ScrollWheelValue!=PrevMouse.ScrollWheelValue){ var d=ms.ScrollWheelValue-PrevMouse.ScrollWheelValue; _scroll-=Math.Sign(d)*(_thumbSize+_spacing)*3; ClampScroll(); } if(ks.IsKeyDown(Keys.PageDown)&&!PrevKeyboard.IsKeyDown(Keys.PageDown)){ _scroll+=(GameView.Instance.GraphicsDevice.Viewport.Height-_thumbSize); ClampScroll(); } if(ks.IsKeyDown(Keys.PageUp)&&!PrevKeyboard.IsKeyDown(Keys.PageUp)){ _scroll-=(GameView.Instance.GraphicsDevice.Viewport.Height-_thumbSize); ClampScroll(); } }
        private IEnumerable<uint> VisibleIds(){ var vp=GameView.Instance.GraphicsDevice.Viewport; _cols=Math.Max(1,(vp.Width-_spacing)/(_thumbSize+_spacing)); int yStart=_scroll/(_thumbSize+_spacing); if(yStart<0) yStart=0; int rowsVisible=vp.Height/(_thumbSize+_spacing)+3; var filtered=string.IsNullOrEmpty(_filter)?_textureIds:_textureIds.Where(i=>($"0x{i:X8}").ToLowerInvariant().Contains(_filter)); int startIndex=yStart*_cols; return filtered.Skip(startIndex).Take(rowsVisible*_cols); }
        private void PumpLoader(){ if(_loadQueue.Count==0) return; var sw=System.Diagnostics.Stopwatch.StartNew(); while(_loadQueue.Count>0){ var id=_loadQueue.Dequeue(); if(_cache.ContainsKey(id)) continue; try{ var tex=BuildTexture(id); if(tex!=null){ _cache[id]=tex; if(_cache.Count>MaxCached){ var first=_cache.Keys.First(); if(first!=id){ _cache[first].Dispose(); _cache.Remove(first);} } } } catch{} if(sw.Elapsed.TotalMilliseconds>LoadBudgetMs) break; } sw.Stop(); }
        private Texture2D BuildTexture(uint id){ var device=GameView.Instance.GraphicsDevice; try{ var type=id>>24; ACE.DatLoader.FileTypes.Texture texFile=null; if(type==0x06){ texFile=DatManager.PortalDat.ReadFromDat<ACE.DatLoader.FileTypes.Texture>(id);} else if(type==0x05){ var st=DatManager.PortalDat.ReadFromDat<SurfaceTexture>(id); if(st?.Textures?.Count>0) texFile=DatManager.PortalDat.ReadFromDat<ACE.DatLoader.FileTypes.Texture>(st.Textures[0]); } else if(type==0x04){ var pal=DatManager.PortalDat.ReadFromDat<Palette>(id); if(pal?.Colors==null||pal.Colors.Count==0) return null; int w=64,h=16; var tex=new Texture2D(device,w,h); var data=new Microsoft.Xna.Framework.Color[w*h]; for(int x=0;x<w;x++){ int idx=(int)((double)x/w*(pal.Colors.Count-1)); var c=pal.Colors[idx]; byte a=(byte)((c>>24)&0xFF); if(a==0)a=255; byte r=(byte)((c>>16)&0xFF); byte g=(byte)((c>>8)&0xFF); byte b=(byte)(c&0xFF); for(int y=0;y<h;y++) data[y*w+x]=new Microsoft.Xna.Framework.Color(r,g,b,a);} tex.SetData(data); return tex; } if(texFile==null) return null; using var bmp=texFile.GetBitmap(); if(bmp==null) return null; int tw=bmp.Width; int th=bmp.Height; double scale=1.0; if(tw>_thumbSize||th>_thumbSize){ scale=Math.Min((double)_thumbSize/tw,(double)_thumbSize/th); tw=(int)(tw*scale); th=(int)(th*scale);} using var resized=(scale!=1.0)?new System.Drawing.Bitmap(bmp,tw,th):new System.Drawing.Bitmap(bmp); var tex2d=new Texture2D(device,tw,th); var colors=new Microsoft.Xna.Framework.Color[tw*th]; for(int y=0;y<th;y++) for(int x=0;x<tw;x++){ var c=resized.GetPixel(x,y); colors[y*tw+x]=new Microsoft.Xna.Framework.Color(c.R,c.G,c.B,c.A==0?(byte)255:c.A);} tex2d.SetData(colors); return tex2d; } catch{ return null; } }
        public void Draw(GameTime time){ var device=GameView.Instance.GraphicsDevice; device.Clear(ConfigManager.Config.BackgroundColors.TextureViewer); var ids=VisibleIds().ToList(); QueueLoads(ids); var sb=GameView.Instance.SpriteBatch; sb.Begin(SpriteSortMode.Deferred,BlendState.NonPremultiplied,SamplerState.PointClamp,null,null); int cellW=_thumbSize+_spacing; int cellH=_thumbSize+_spacing; int col=0,row=0; int startY=-(_scroll%cellH); foreach(var id in ids){ int x=_spacing+col*cellW; int y=startY+row*cellH; if(_cache.TryGetValue(id,out var tex)&&tex!=null){ sb.Draw(tex,new Rectangle(x,y,tex.Width,tex.Height),Microsoft.Xna.Framework.Color.White);} DrawRect(sb,x-1,y-1,_thumbSize+2,_thumbSize+2,Microsoft.Xna.Framework.Color.DimGray); sb.DrawString(_font,$"{id:X8}",new Vector2(x,y+_thumbSize-14),Microsoft.Xna.Framework.Color.White); col++; if(col>=_cols){ col=0; row++; } } sb.End(); int totalRows=(int)Math.Ceiling((double)FilteredCount()/_cols); _maxScroll=Math.Max(0,totalRows*cellH-device.Viewport.Height+cellH); ClampScroll(); }
        private int FilteredCount(){ if(string.IsNullOrEmpty(_filter)) return _textureIds.Count; return _textureIds.Count(i=>($"0x{i:X8}").ToLowerInvariant().Contains(_filter)); }
        private void QueueLoads(IEnumerable<uint> ids){ foreach(var id in ids){ if(_cache.ContainsKey(id)) continue; if(_loadQueue.Contains(id)) continue; _loadQueue.Enqueue(id);} }
        private Texture2D _pixel; bool _pixInit; private void DrawRect(SpriteBatch sb,int x,int y,int w,int h,Microsoft.Xna.Framework.Color c){ _pixel ??= new Texture2D(GameView.Instance.GraphicsDevice,1,1); if(!_pixInit){ _pixel.SetData(new[]{Microsoft.Xna.Framework.Color.White}); _pixInit=true;} sb.Draw(_pixel,new Rectangle(x,y,w,1),c); sb.Draw(_pixel,new Rectangle(x,y+h-1,w,1),c); sb.Draw(_pixel,new Rectangle(x,y,1,h),c); sb.Draw(_pixel,new Rectangle(x+w-1,y,1,h),c); }
        private void ClampScroll(){ if(_scroll<0)_scroll=0; if(_scroll>_maxScroll)_scroll=_maxScroll; }
    }
}
