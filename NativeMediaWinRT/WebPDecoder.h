#pragma once

#include "WebPFrame.g.h"
#include "WebPAnimation.g.h"
#include "WebPDecoder.g.h"

namespace winrt::NativeMediaWinRT::implementation
{
    struct WebPFrame : WebPFrameT<WebPFrame>
    {
        WebPFrame() = default;
        WebPFrame(com_array<uint8_t> const& bgraBuffer, int32_t durationMs);

        com_array<uint8_t> BgraBuffer();
        int32_t DurationMs();

    private:
        com_array<uint8_t> m_bgraBuffer;
        int32_t m_durationMs{ 0 };
    };

    struct WebPAnimation : WebPAnimationT<WebPAnimation>
    {
        WebPAnimation() = default;
        WebPAnimation(int32_t width, int32_t height, int32_t frameCount, int32_t loopCount, Windows::Foundation::Collections::IVectorView<NativeMediaWinRT::WebPFrame> const& frames);

        int32_t Width();
        int32_t Height();
        int32_t FrameCount();
        int32_t LoopCount();
        Windows::Foundation::Collections::IVectorView<NativeMediaWinRT::WebPFrame> Frames();

    private:
        int32_t m_width{ 0 };
        int32_t m_height{ 0 };
        int32_t m_frameCount{ 0 };
        int32_t m_loopCount{ 0 };
        Windows::Foundation::Collections::IVectorView<NativeMediaWinRT::WebPFrame> m_frames;
    };

    struct WebPDecoder : WebPDecoderT<WebPDecoder>
    {
        WebPDecoder() = default;

        static NativeMediaWinRT::WebPAnimation DecodeAnimation(hstring const& filePath);
    };
}

namespace winrt::NativeMediaWinRT::factory_implementation
{
    struct WebPFrame : WebPFrameT<WebPFrame, implementation::WebPFrame>
    {
    };

    struct WebPAnimation : WebPAnimationT<WebPAnimation, implementation::WebPAnimation>
    {
    };

    struct WebPDecoder : WebPDecoderT<WebPDecoder, implementation::WebPDecoder>
    {
    };
}
