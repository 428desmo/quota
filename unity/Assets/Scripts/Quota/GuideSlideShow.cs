using UnityEngine;
using UnityEngine.UI;

namespace Quota
{
    public sealed class GuideSlideShow : MonoBehaviour
    {
        const float SlideDuration = 5f;
        Sprite[] slides;
        Image image;
        Text counter;
        Text pauseLabel;
        float timeInSlide;
        int shown = -1;
        bool paused;

        public void Initialize(Image target, Text pageCounter, Sprite[] pictures)
        {
            image = target;
            counter = pageCounter;
            slides = pictures;
            timeInSlide = 0f;
            paused = false;
            ShowAt(0f);
        }

        void Update()
        {
            Tick(Time.unscaledDeltaTime);
        }

        public void SetPauseLabel(Text label)
        {
            pauseLabel = label;
            UpdatePauseLabel();
        }

        public void Previous() { Step(-1); }
        public void Next() { Step(1); }

        void Step(int direction)
        {
            if (slides == null || slides.Length == 0) return;
            timeInSlide = 0f;
            ShowIndex((shown + direction + slides.Length) % slides.Length);
        }

        public void TogglePause()
        {
            paused = !paused;
            UpdatePauseLabel();
        }

        void UpdatePauseLabel()
        {
            if (pauseLabel != null) pauseLabel.text = paused ? "▶" : "Ⅱ";
        }

        public void Tick(float seconds)
        {
            if (paused || slides == null || slides.Length == 0 || seconds <= 0f) return;
            timeInSlide += seconds;
            if (timeInSlide < SlideDuration) return;
            var steps = Mathf.FloorToInt(timeInSlide / SlideDuration);
            timeInSlide %= SlideDuration;
            ShowIndex((shown + steps) % slides.Length);
        }

        public void ShowAt(float elapsed)
        {
            if (slides == null || slides.Length == 0 || image == null) return;
            var safeElapsed = Mathf.Max(0f, elapsed);
            var index = Mathf.FloorToInt(safeElapsed / SlideDuration) % slides.Length;
            timeInSlide = safeElapsed % SlideDuration;
            ShowIndex(index);
        }

        void ShowIndex(int index)
        {
            if (index == shown) return;
            shown = index;
            image.sprite = slides[index];
            image.preserveAspect = true;
            if (counter != null) counter.text = (index + 1) + " / " + slides.Length;
        }
    }
}
