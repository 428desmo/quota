using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Quota
{
    // Independent visual prototype: no game scores or live bonus display are changed.
    public class BonusChipLab : MaskableGraphic
    {
        public class Chip
        {
            public Vector2 Position;
            public Quaternion Rotation;
            public Color Color;
            public int Sides;
            public float Size, Aspect, Thickness, Born;
        }
        public class State
        {
            public int Shape, ColorIndex, Sides = 6;
            public float Size = 18, Aspect = 0.65f, Thickness = 4;
            public readonly List<Chip> Chips = new List<Chip>();
            readonly System.Random random = new System.Random();
            public void Add(int count, float width, float height, float now)
            {
                var colors = new[] { new Color(0.22f,0.72f,0.30f), new Color(0.20f,0.49f,0.91f), new Color(0.62f,0.28f,0.83f) };
                for (var i = 0; i < count; i++)
                {
                    var margin = Size + Thickness;
                    Chips.Add(new Chip {
                        Position = new Vector2(margin + (float)random.NextDouble() * Mathf.Max(0,width-2*margin), margin + (float)random.NextDouble() * Mathf.Max(0,height-2*margin)),
                        Rotation = Quaternion.Euler((float)random.NextDouble()*35-17, (float)random.NextDouble()*35-17, (float)random.NextDouble()*360),
                        Color = colors[ColorIndex], Sides = Shape < 2 ? 32 : Shape == 2 ? Sides : 4,
                        Size = Size, Aspect = Shape == 1 || Shape == 3 ? Aspect : 1, Thickness = Thickness,
                        Born = now + i * 0.012f
                    });
                }
            }
        }
        State state;
        int first;
        readonly List<Face> faces = new List<Face>();
        class Face { public Vector3[] Points; public Color Color; public float Depth; }
        static readonly Vector3 Light = new Vector3(-0.6f, 0.8f, 1.2f).normalized;
        public static void Open(Transform parent, float width, float height, bool wide, bool titled, Font font, Color fill, Color ink, State state, UnityAction exit)
        {
            var panel = Portrait.Box(parent, "bonus-chip-test", 20, 20, width-40, height-40, 7, 1, fill, ink, false);
            var pw = width-40;
            Label(panel,"ボーナスチップ実装テスト",20,24,pw-40,52,32,font,ink);
            var trayW = wide ? 176f : 220f;
            var trayH = wide ? (titled ? 126f : 148f) : 105f;
            var tray = Portrait.Box(panel,"chip-tray",(pw-trayW)/2,(height-40-trayH)/2,trayW,trayH,7,1,fill,ink,false);
            tray.gameObject.AddComponent<RectMask2D>();
            var draw = Portrait.Rect(tray,"chips",0,0,trayW,trayH).gameObject.AddComponent<BonusChipLab>();
            draw.state = state; draw.raycastTarget = false;
            var count = Label(panel,"",20,88,pw-40,40,24,font,ink);
            Action update = () => {
                count.text = $"チップ {state.Chips.Count}枚　トレイ {trayW}×{trayH}";
                // Split meshes to stay below Unity UI's 65k vertex limit.
                for (var offset = 400; offset < state.Chips.Count; offset += 400)
                {
                    if (tray.Find("chips" + offset) != null) continue;
                    var batch = Portrait.Rect(tray,"chips" + offset,0,0,trayW,trayH).gameObject.AddComponent<BonusChipLab>();
                    batch.state = state; batch.first = offset; batch.raycastTarget = false;
                }
                foreach (var batch in tray.GetComponentsInChildren<BonusChipLab>()) batch.SetVerticesDirty();
            };
            var buttonW = wide ? 600f : Mathf.Min(800,pw-80);
            var bx = wide ? 40f : (pw-buttonW)/2;
            var y = 150f;
            var shapes = new[] { "円形", "楕円形", "正多角柱", "板状直方体" };
            Cycle(panel,bx,y,buttonW,"形状",()=>shapes[state.Shape],()=>state.Shape=(state.Shape+1)%4,font,ink,fill); y+=70;
            Cycle(panel,bx,y,buttonW,"サイズ（直径／幅）",()=>state.Size.ToString("0"),()=>state.Size=state.Size>=30?10:state.Size+4,font,ink,fill); y+=70;
            Cycle(panel,bx,y,buttonW,"厚み",()=>state.Thickness.ToString("0"),()=>state.Thickness=state.Thickness>=10?2:state.Thickness+2,font,ink,fill); y+=70;
            Cycle(panel,bx,y,buttonW,"多角形の辺数",()=>state.Sides.ToString(),()=>state.Sides=state.Sides>=8?3:state.Sides+1,font,ink,fill); y+=70;
            Cycle(panel,bx,y,buttonW,"縦横比（楕円・板）",()=>state.Aspect.ToString("0.00"),()=>state.Aspect=state.Aspect>=0.99f?0.4f:Mathf.Min(1,state.Aspect+0.15f),font,ink,fill); y+=70;
            Cycle(panel,bx,y,buttonW,"色",()=>new[]{"緑","青","紫"}[state.ColorIndex],()=>state.ColorIndex=(state.ColorIndex+1)%3,font,ink,fill);
            var bottom = height-40-180;
            for (var i=0;i<3;i++)
            {
                var n = new[]{1,10,50}[i];
                Button(panel,"＋"+n,bx+i*(buttonW/3),bottom,buttonW/3-12,60,()=>{state.Add(n,trayW,trayH,Time.unscaledTime);update();},font,ink,fill);
            }
            Button(panel,"リセット",bx,bottom+80,buttonW/2-12,60,()=>{state.Chips.Clear();update();},font,ink,fill);
            Button(panel,"終了",bx+buttonW/2,bottom+80,buttonW/2-12,60,exit,font,ink,fill);
            update();
        }
        static Text Label(Transform parent,string text,float x,float y,float w,float h,int size,Font font,Color ink)
        {
            var t=Portrait.Rect(parent,text,x,y,w,h).gameObject.AddComponent<Text>();
            t.text=text;t.font=font;t.fontSize=size;t.color=ink;t.alignment=TextAnchor.MiddleCenter;t.raycastTarget=false;return t;
        }
        static void Button(Transform p,string name,float x,float y,float w,float h,UnityAction action,Font font,Color ink,Color fill)
        {
            var r=Portrait.Box(p,name,x,y,w,h,7,1,fill,ink,false);
            var hit=r.gameObject.AddComponent<Button>();hit.targetGraphic=r.GetComponent<Image>();r.GetComponent<Image>().raycastTarget=true;hit.onClick.AddListener(action);
            Label(r,name,4,0,w-8,h,24,font,ink);
        }
        static void Cycle(Transform p,float x,float y,float w,string title,Func<string> value,Action next,Font font,Color ink,Color fill)
        {
            var label=Label(p,title+"："+value(),x,y,w-100,56,24,font,ink);
            Button(p,"変更",x+w-100,y,100,56,()=>{next();label.text=title+"："+value();},font,ink,fill);
        }
        void Update()
        {
            if(state == null || state.Chips.Count==0)return;
            if(Time.unscaledTime < state.Chips[state.Chips.Count-1].Born+0.65f)SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();if(state==null)return;
            for(var k=first;k<Mathf.Min(first+400,state.Chips.Count);k++)
            {
                var chip=state.Chips[k];
                var t=Mathf.Clamp01((Time.unscaledTime-chip.Born)/0.6f);
                if(Time.unscaledTime<chip.Born)continue;
                var drop=(1-t)*(1-t)*55f;
                var center=new Vector2(chip.Position.x, -chip.Position.y+drop+Mathf.Sin(t*Mathf.PI*3)*(1-t)*5);
                var top=new Vector3[chip.Sides];var bottom=new Vector3[chip.Sides];
                for(var i=0;i<chip.Sides;i++)
                {
                    var angle=2*Mathf.PI*i/chip.Sides+(chip.Sides==4?Mathf.PI/4:0);
                    var radius=chip.Size/2*(chip.Sides==4?Mathf.Sqrt(2):1);
                    var v=new Vector3(Mathf.Cos(angle)*radius,Mathf.Sin(angle)*radius*chip.Aspect,0);
                    bottom[i]=chip.Rotation*v;top[i]=chip.Rotation*(v+Vector3.forward*chip.Thickness);
                }
                faces.Clear();
                AddFace(top,chip.Rotation*Vector3.forward,chip.Color);
                for(var i=0;i<chip.Sides;i++)
                {
                    var j=(i+1)%chip.Sides;
                    var normal=Vector3.Cross(bottom[i]-top[i],top[j]-top[i]).normalized;
                    if(normal.z>0)AddFace(new[]{top[i],bottom[i],bottom[j],top[j]},normal,chip.Color);
                }
                faces.Sort((a,b)=>a.Depth.CompareTo(b.Depth));
                foreach(var f in faces)
                {
                    var start=vh.currentVertCount;
                    foreach(var v in f.Points)vh.AddVert(new Vector3(center.x+v.x,center.y+v.y*0.8f-v.z*0.35f),f.Color,Vector2.zero);
                    for(var i=1;i<f.Points.Length-1;i++)vh.AddTriangle(start,start+i,start+i+1);
                }
            }
        }
        void AddFace(Vector3[] points,Vector3 normal,Color tint)
        {
            var brightness=0.40f+0.60f*Mathf.Max(0,Vector3.Dot(normal,Light));
            var depth=0f;foreach(var p in points)depth+=p.z;
            faces.Add(new Face{Points=points,Color=new Color(tint.r*brightness,tint.g*brightness,tint.b*brightness,1),Depth=depth/points.Length});
        }
    }
}
