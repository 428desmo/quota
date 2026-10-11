using UnityEngine;

namespace Quota
{
    public sealed partial class TableView
    {
        AudioSource[] cardSe;
        AudioSource chipCountingSe;
        Coroutine chipCountingFade;

        float SeGain => seLevel / 5f;

        void InitializeCardSe()
        {
            cardSe = new AudioSource[3];
            for (var i = 0; i < cardSe.Length; i++)
            {
                var clip = Resources.Load<AudioClip>("QuotaSe/card_0" + (i + 1));
                if (clip == null)
                {
                    Debug.LogWarning("Quota card SE clip is missing: card_0" + (i + 1));
                    continue;
                }
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.clip = clip;
                source.volume = SeGain;
                cardSe[i] = source;
            }
            var countingClip = Resources.Load<AudioClip>("QuotaSe/chip_counting");
            if (countingClip != null)
            {
                chipCountingSe = gameObject.AddComponent<AudioSource>();
                chipCountingSe.playOnAwake = false;
                chipCountingSe.spatialBlend = 0f;
                chipCountingSe.loop = true;
                chipCountingSe.clip = countingClip;
                chipCountingSe.volume = SeGain;
            }
            else Debug.LogWarning("Quota chip-counting SE clip is missing.");
        }

        void SetCardSeGain()
        {
            if (cardSe != null)
                foreach (var source in cardSe)
                    if (source != null) source.volume = SeGain;
            if (seLevel == 0)
            {
                StopChipCountingSe(true);
                return;
            }
            if (chipCountingSe != null && chipCountingFade == null) chipCountingSe.volume = SeGain;
        }

        void PlayCardSe(int index)
        {
            if (cardSe == null || index < 0 || index >= cardSe.Length) return;
            var source = cardSe[index];
            if (source != null && seLevel > 0) source.Play();
        }

        void StartChipCountingSe()
        {
            if (chipCountingFade != null)
            {
                StopCoroutine(chipCountingFade);
                chipCountingFade = null;
            }
            if (chipCountingSe == null || seLevel == 0) return;
            chipCountingSe.volume = SeGain;
            if (!chipCountingSe.isPlaying) chipCountingSe.Play();
        }

        void StopChipCountingSe(bool immediate = false)
        {
            if (chipCountingFade != null)
            {
                StopCoroutine(chipCountingFade);
                chipCountingFade = null;
            }
            if (chipCountingSe == null || !chipCountingSe.isPlaying) return;
            if (immediate || !Application.isPlaying) chipCountingSe.Stop();
            else chipCountingFade = StartCoroutine(FadeOutChipCountingSe());
        }

        System.Collections.IEnumerator FadeOutChipCountingSe()
        {
            var from = chipCountingSe.volume;
            var elapsed = 0f;
            while (elapsed < 0.1f && chipCountingSe != null && chipCountingSe.isPlaying)
            {
                elapsed += Time.unscaledDeltaTime;
                chipCountingSe.volume = from * (1f - Mathf.Clamp01(elapsed / 0.1f));
                yield return null;
            }
            if (chipCountingSe != null) chipCountingSe.Stop();
            chipCountingFade = null;
        }
    }
}
