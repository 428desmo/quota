using UnityEngine;

namespace Quota
{
    public sealed partial class TableView
    {
        AudioSource bgmIntro;
        AudioSource bgmLoop;
        AudioClip bgmIntroClip;
        AudioClip bgmLoopClip;
        int bgmRound;
        bool bgmActive;

        void InitializeRoundBgm()
        {
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
            bgmLoop.clip = bgmLoopClip;
            bgmLoop.loop = true;
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
            if (bgmIntro == null || bgmLoop == null) return;
            bgmRound = game.RoundIndex;
            bgmActive = true;
            // The source MP3 is cut at 16 s and 92 s during asset preparation.
            // Scheduling the second clip avoids a frame-dependent seek at the seam.
            var start = AudioSettings.dspTime + 0.05;
            bgmIntro.PlayScheduled(start);
            bgmLoop.PlayScheduled(start + bgmIntroClip.length);
        }

        void StopRoundBgm()
        {
            if (!bgmActive) return;
            if (bgmIntro != null) bgmIntro.Stop();
            if (bgmLoop != null) bgmLoop.Stop();
            bgmActive = false;
            bgmRound = 0;
        }
    }
}
