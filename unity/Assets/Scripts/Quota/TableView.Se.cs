using UnityEngine;

namespace Quota
{
    public sealed partial class TableView
    {
        AudioSource[] cardSe;

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
        }

        void SetCardSeGain()
        {
            if (cardSe == null) return;
            foreach (var source in cardSe)
                if (source != null) source.volume = SeGain;
        }

        void PlayCardSe(int index)
        {
            if (cardSe == null || index < 0 || index >= cardSe.Length) return;
            var source = cardSe[index];
            if (source != null && seLevel > 0) source.Play();
        }
    }
}
