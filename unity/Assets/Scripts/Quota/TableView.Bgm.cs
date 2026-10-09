using UnityEngine;

namespace Quota
{
    public sealed partial class TableView
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        static extern void QuotaBgmControl(int action, string url);

        static string BgmUrl() => Application.streamingAssetsPath + "/play_bgm_01.mp3";
#endif
        AudioSource bgmIntro;
        AudioSource bgmLoop;
        AudioClip bgmIntroClip;
        AudioClip bgmLoopClip;
        AudioClip bgmPcmClip;
        int bgmRound;
        bool bgmActive;

        void InitializeRoundBgm()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            QuotaBgmControl(0, BgmUrl());
#else
            bgmIntroClip = Resources.Load<AudioClip>("QuotaBgm/intro");
            bgmLoopClip = Resources.Load<AudioClip>("QuotaBgm/loop");
            if (bgmIntroClip == null || bgmLoopClip == null)
            {
                Debug.LogWarning("Quota BGM clips are missing; the round will play silently.");
                return;
            }
            bgmIntro = gameObject.AddComponent<AudioSource>();
            bgmIntro.playOnAwake = false;
            bgmIntro.spatialBlend = 0f;
            bgmIntro.clip = bgmIntroClip;
            bgmLoop = gameObject.AddComponent<AudioSource>();
            bgmLoop.playOnAwake = false;
            bgmLoop.spatialBlend = 0f;
            // Loop decoded PCM samples rather than asking the audio source to
            // restart a compressed Vorbis stream at every repeat.
            var samples = new float[bgmLoopClip.samples * bgmLoopClip.channels];
            if (bgmLoopClip.GetData(samples, 0))
            {
                var pcmLoop = AudioClip.Create("Quota BGM loop PCM", bgmLoopClip.samples,
                    bgmLoopClip.channels, bgmLoopClip.frequency, false);
                pcmLoop.SetData(samples, 0);
                bgmPcmClip = pcmLoop;
                bgmLoopClip = pcmLoop;
            }
            else Debug.LogWarning("Quota BGM loop could not be decoded to PCM.");
            bgmLoop.clip = bgmLoopClip;
            bgmLoop.loop = true;
#endif
        }

        void SyncRoundBgm()
        {
            var game = match.Game;
            var playingRound = !onSetup && !reviewMode && game != null && !game.AwaitingNextRound && !game.Finished;
            if (!playingRound)
            {
                StopRoundBgm();
                return;
            }
            if (bgmActive && bgmRound == game.RoundIndex) return;
            StopRoundBgm();
#if !UNITY_WEBGL || UNITY_EDITOR
            if (bgmIntro == null || bgmLoop == null) return;
#endif
            bgmRound = game.RoundIndex;
            bgmActive = true;
#if UNITY_WEBGL && !UNITY_EDITOR
            QuotaBgmControl(1, BgmUrl());
#else
            // The source MP3 is cut at 16 s and 92 s during asset preparation.
            // Scheduling the second clip avoids a frame-dependent seek at the seam.
            var start = AudioSettings.dspTime + 0.05;
            bgmIntro.PlayScheduled(start);
            bgmLoop.PlayScheduled(start + bgmIntroClip.length);
#endif
        }

        void StopRoundBgm()
        {
            if (!bgmActive) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            QuotaBgmControl(2, "");
#else
            if (bgmIntro != null) bgmIntro.Stop();
            if (bgmLoop != null) bgmLoop.Stop();
#endif
            bgmActive = false;
            bgmRound = 0;
        }

        void DisposeRoundBgm()
        {
            StopRoundBgm();
            if (bgmPcmClip == null) return;
            if (Application.isPlaying) Destroy(bgmPcmClip);
            else DestroyImmediate(bgmPcmClip);
            bgmPcmClip = null;
        }
    }
}
