using UnityEngine;

namespace Riverworks
{
    public sealed class Soundscape : MonoBehaviour
    {
        public enum Cue { Click, Build, Expand, Denied }
        AudioSource source;
        AudioClip[] clips;
        void Awake()
        {
            source=gameObject.AddComponent<AudioSource>(); source.playOnAwake=false; source.volume=.23f;
            AudioListener.volume=.45f;
            clips=new[]{Tone("Click",new[]{660f},.07f),Tone("Build",new[]{523.25f,659.25f,783.99f},.24f),Tone("District acquired",new[]{523.25f,659.25f,783.99f,1046.5f},.65f),Tone("Unavailable",new[]{196f,185f},.13f)};
        }
        public void Play(Cue cue) { if(source && clips!=null) source.PlayOneShot(clips[(int)cue]); }
        static AudioClip Tone(string name,float[] notes,float length)
        {
            const int rate=22050; int count=(int)(length*rate); var data=new float[count];
            for(int i=0;i<count;i++)
            {
                float t=(float)i/rate,value=0;
                for(int n=0;n<notes.Length;n++) { float time=t-n*.038f; if(time>=0) value+=Mathf.Sin(2*Mathf.PI*notes[n]*time)*Mathf.Exp(-time*9)*Mathf.Clamp01(time*150)/notes.Length; }
                data[i]=value*.5f*Mathf.Clamp01((length-t)*40);
            }
            var clip=AudioClip.Create(name,count,1,rate,false);clip.SetData(data,0);return clip;
        }
        void OnDestroy() { if(clips!=null) foreach(var clip in clips) if(clip) Destroy(clip); }
    }
}
