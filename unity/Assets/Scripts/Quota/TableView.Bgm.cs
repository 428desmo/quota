using UnityEngine;

namespace Quota
{
    public sealed partial class TableView
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        static extern void QuotaBgmControl(int action, string url, float gain);

        static string BgmUrl() => Application.streamingAssetsPath + "/play_bgm_01.mp3";
#endif
        AudioSource bgmIntro;
        AudioSource bgmLoop;
        AudioSource bgmOutro;
        bool endBgmFading;
        AudioClip bgmIntroClip;
        AudioClip bgmLoopClip;
        AudioClip bgmPcmClip;
        int bgmRound;
        bool bgmActive;
        bool bgmEnding;
        int bgmLevel = 5;
        int seLevel = 5;

        float BgmGain => bgmLevel / 5f;

        void SetRoundBgmGain(float gain)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            QuotaBgmControl(3, "", Mathf.Clamp01(gain));
#else
            if (bgmIntro != null) bgmIntro.volume = Mathf.Clamp01(gain);
            if (bgmLoop != null) bgmLoop.volume = Mathf.Clamp01(gain);
#endif
        }

        System.Collections.IEnumerator FadeOutRoundBgm(int serial)
        {
            var from = BgmGain;
            var elapsed = 0f;
            while (elapsed < 0.5f && serial == cpuRun && bgmActive)
            {
                elapsed += Time.unscaledDeltaTime;
                SetRoundBgmGain(from * (1f - Mathf.Clamp01(elapsed / 0.5f)));
                yield return null;
            }
            if (serial == cpuRun) StopRoundBgm();
        }

        void InitializeRoundBgm()
        {
            var outroClip = Resources.Load<AudioClip>("QuotaBgm/end");
            if (outroClip != null)
            {
                bgmOutro = gameObject.AddComponent<AudioSource>();
                bgmOutro.playOnAwake = false;
                bgmOutro.spatialBlend = 0f;
                bgmOutro.loop = false;
                bgmOutro.clip = outroClip;
                bgmOutro.volume = BgmGain;
            }
            else Debug.LogWarning("Quota round-end BGM clip is missing.");
#if UNITY_WEBGL && !UNITY_EDITOR
            QuotaBgmControl(0, BgmUrl(), BgmGain);
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
            var pendingVerdict = !onSetup && !reviewMode && game != null
                && (game.AwaitingNextRound || (game.Finished && !ceremonyDismissed))
                && acknowledgedRound != game.RoundIndex;
            if (!playingRound)
            {
                if (!bgmEnding && !pendingVerdict) StopRoundBgm();
                return;
            }
            if (bgmActive && bgmRound == game.RoundIndex) return;
            if (EndBgmNeedsFade() && !endBgmFading)
                StartCoroutine(FadeOutEndBgm());
            if (endBgmFading) return;
            StopEndBgm();
            StopRoundBgm();
#if !UNITY_WEBGL || UNITY_EDITOR
            if (bgmIntro == null || bgmLoop == null) return;
#endif
            bgmRound = game.RoundIndex;
            bgmActive = true;
            bgmEnding = false;
            SetRoundBgmGain(BgmGain);
#if UNITY_WEBGL && !UNITY_EDITOR
            QuotaBgmControl(1, BgmUrl(), BgmGain);
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
            bgmEnding = false;
            if (!bgmActive) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            QuotaBgmControl(2, "", 0f);
#else
            if (bgmIntro != null) bgmIntro.Stop();
            if (bgmLoop != null) bgmLoop.Stop();
#endif
            bgmActive = false;
            bgmRound = 0;
        }

        void StartEndBgm()
        {
            if (bgmOutro == null) return;
            bgmOutro.Stop();
            bgmOutro.volume = BgmGain;
            bgmOutro.Play();
        }

        void StopEndBgm()
        {
            if (bgmOutro != null) bgmOutro.Stop();
            endBgmFading = false;
        }

        bool EndBgmNeedsFade() => bgmOutro != null && bgmOutro.isPlaying && bgmOutro.volume > 0f;

        System.Collections.IEnumerator FadeOutEndBgm()
        {
            endBgmFading = true;
            var from = bgmOutro.volume;
            var elapsed = 0f;
            while (elapsed < 0.5f && bgmOutro != null && bgmOutro.isPlaying)
            {
                elapsed += Time.unscaledDeltaTime;
                bgmOutro.volume = from * (1f - Mathf.Clamp01(elapsed / 0.5f));
                yield return null;
            }
            StopEndBgm();
        }

        void SetEndBgmGain(float gain)
        {
            if (bgmOutro != null) bgmOutro.volume = Mathf.Clamp01(gain);
        }

        void DisposeRoundBgm()
        {
            StopEndBgm();
            StopRoundBgm();
            if (bgmPcmClip == null) return;
            if (Application.isPlaying) Destroy(bgmPcmClip);
            else DestroyImmediate(bgmPcmClip);
            bgmPcmClip = null;
        }
    }
}
