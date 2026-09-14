using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Riverworks
{
    /// <summary>Collider-free survey beacons for the four fixed industrial deposits.</summary>
    internal sealed class IndustryDepositView : MonoBehaviour
    {
        sealed class Marker { public int X,Z; public Transform Root; public TextMesh Caption; }
        readonly List<Marker> markers=new List<Marker>();
        GameController game;
        FactoryVisuals.Palette palette;

        public void Initialize(GameController controller,FactoryVisuals.Palette materials)
        {
            game=controller; palette=materials;
            Add(16,10); Add(10,16); Add(16,16); Add(16,4);
            Refresh();
        }

        void Add(int x,int z)
        {
            Resource resource=IndustryDeposits.At(x,z);
            if(resource==Resource.Coins) return;
            var root=new GameObject("Survey "+resource+" "+x+","+z).transform; root.SetParent(transform,false);
            root.localPosition=BoardView.Position(x,z)+Vector3.up*.14f;
            Material color=palette.ForResource(resource);
            Cylinder(root,"Survey ring",.34f,.035f,Vector3.zero,color);
            for(int i=0;i<3;i++)
            {
                float angle=i*Mathf.PI*2f/3f;
                Box(root,"Survey stake",new Vector3(.055f,.32f,.055f),new Vector3(Mathf.Cos(angle)*.29f,.16f,Mathf.Sin(angle)*.29f),color);
            }
            var gem=Box(root,"Deposit sample",new Vector3(.22f,.22f,.22f),new Vector3(0,.19f,0),color);
            gem.transform.localRotation=Quaternion.Euler(25,45,20);
            var label=new GameObject("Close zoom caption").AddComponent<TextMesh>(); label.transform.SetParent(root,false);
            label.transform.localPosition=new Vector3(0,.58f,0); label.anchor=TextAnchor.MiddleCenter; label.alignment=TextAlignment.Center;
            label.fontSize=30; label.characterSize=.035f; label.color=Color.white;
            var spec=ResourceCatalog.Get(resource); label.text=spec==null?resource.ToString():spec.Name;
            label.GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.Off;
            markers.Add(new Marker{X=x,Z=z,Root=root,Caption=label});
        }

        public void Refresh()
        {
            if(game==null) return;
            bool close=game.CameraRig!=null&&game.CameraRig.Camera!=null&&game.CameraRig.Camera.orthographicSize<=7.5f;
            for(int i=0;i<markers.Count;i++)
            {
                var marker=markers[i]; bool owned=game.Sim!=null&&game.Sim.IsOwned(marker.X,marker.Z);
                marker.Root.localScale=Vector3.one*(owned?1f:.72f);
                marker.Caption.gameObject.SetActive(close);
                if(close&&game.CameraRig!=null) marker.Caption.transform.rotation=game.CameraRig.Camera.transform.rotation;
            }
        }

        static GameObject Box(Transform parent,string name,Vector3 scale,Vector3 position,Material material)
        {
            var o=GameObject.CreatePrimitive(PrimitiveType.Cube); o.name=name; o.transform.SetParent(parent,false); o.transform.localScale=scale; o.transform.localPosition=position;
            o.GetComponent<Renderer>().sharedMaterial=material; Strip(o); return o;
        }
        static GameObject Cylinder(Transform parent,string name,float radius,float height,Vector3 position,Material material)
        {
            var o=GameObject.CreatePrimitive(PrimitiveType.Cylinder); o.name=name; o.transform.SetParent(parent,false); o.transform.localScale=new Vector3(radius,height*.5f,radius); o.transform.localPosition=position;
            o.GetComponent<Renderer>().sharedMaterial=material; Strip(o); return o;
        }
        static void Strip(GameObject o) { var c=o.GetComponent<Collider>(); if(c) { c.enabled=false; Object.Destroy(c); } }
    }
}
