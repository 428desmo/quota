using UnityEngine;
using UnityEngine.UI;

namespace Quota
{
    public sealed class GuideSlideShow : MonoBehaviour
    {
        Sprite[] slides;
        Image image;
        Text counter;
        float started;
        int shown = -1;

        public void Initialize(Image target, Text pageCounter, Sprite[] pictures)
        {
            image = target;
            counter = pageCounter;
            slides = pictures;
            started = Time.unscaledTime;
            ShowAt(0f);
        }

        void Update()
        {
            ShowAt(Time.unscaledTime - started);
        }

        public void ShowAt(float elapsed)
        {
            if (slides == null || slides.Length == 0 || image == null) return;
            var index = Mathf.FloorToInt(Mathf.Max(0f, elapsed) / 2f) % slides.Length;
            if (index == shown) return;
            shown = index;
            image.sprite = slides[index];
            image.preserveAspect = true;
            if (counter != null) counter.text = (index + 1) + " / " + slides.Length;
        }
    }
}
