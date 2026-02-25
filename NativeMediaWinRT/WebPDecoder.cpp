#include "pch.h"
#include "WebPDecoder.h"
#if __has_include("WebPFrame.g.cpp")
#include "WebPFrame.g.cpp"
#endif
#if __has_include("WebPAnimation.g.cpp")
#include "WebPAnimation.g.cpp"
#endif
#if __has_include("WebPDecoder.g.cpp")
#include "WebPDecoder.g.cpp"
#endif
#include <fstream>
#include <vector>
#include <webp/decode.h>
#include <webp/demux.h>

namespace winrt::NativeMediaWinRT::implementation
{
    WebPFrame::WebPFrame(com_array<uint8_t> const& bgraBuffer, int32_t durationMs)
        : m_bgraBuffer(bgraBuffer.begin(), bgraBuffer.end()), m_durationMs(durationMs)
    {
    }

    com_array<uint8_t> WebPFrame::BgraBuffer()
    {
        return com_array<uint8_t>(m_bgraBuffer.begin(), m_bgraBuffer.end());
    }

    int32_t WebPFrame::DurationMs()
    {
        return m_durationMs;
    }

    WebPAnimation::WebPAnimation(int32_t width, int32_t height, int32_t frameCount, int32_t loopCount, Windows::Foundation::Collections::IVectorView<NativeMediaWinRT::WebPFrame> const& frames)
        : m_width(width), m_height(height), m_frameCount(frameCount), m_loopCount(loopCount), m_frames(frames)
    {
    }

    int32_t WebPAnimation::Width()
    {
        return m_width;
    }

    int32_t WebPAnimation::Height()
    {
        return m_height;
    }

    int32_t WebPAnimation::FrameCount()
    {
        return m_frameCount;
    }

    int32_t WebPAnimation::LoopCount()
    {
        return m_loopCount;
    }

    Windows::Foundation::Collections::IVectorView<NativeMediaWinRT::WebPFrame> WebPAnimation::Frames()
    {
        return m_frames;
    }

    NativeMediaWinRT::WebPAnimation WebPDecoder::DecodeAnimation(hstring const& filePath)
    {
        std::ifstream file(filePath.c_str(), std::ios::binary | std::ios::ate);
        if (!file.is_open()) return nullptr;

        std::streamsize size = file.tellg();
        file.seekg(0, std::ios::beg);

        std::vector<uint8_t> buffer(size);
        if (!file.read(reinterpret_cast<char*>(buffer.data()), size)) return nullptr;

        WebPData webpData = { buffer.data(), buffer.size() };
        WebPAnimDecoderOptions decOptions;
        if (!WebPAnimDecoderOptionsInit(&decOptions)) return nullptr;

        decOptions.color_mode = MODE_BGRA;

        WebPAnimDecoder* decoder = WebPAnimDecoderNew(&webpData, &decOptions);
        if (!decoder) return nullptr;

        WebPAnimInfo animInfo;
        if (!WebPAnimDecoderGetInfo(decoder, &animInfo)) {
            WebPAnimDecoderDelete(decoder);
            return nullptr;
        }

        std::vector<NativeMediaWinRT::WebPFrame> frames;
        int frameIndex = 0;
        uint8_t* buf;
        int timestamp = 0;
        int prevTimestamp = 0;

        while (WebPAnimDecoderGetNext(decoder, &buf, &timestamp)) {
            if (frameIndex >= static_cast<int>(animInfo.frame_count)) break;

            size_t frameSize = static_cast<size_t>(animInfo.canvas_width) * animInfo.canvas_height * 4;
            com_array<uint8_t> frameBuffer(frameSize);
            memcpy(frameBuffer.data(), buf, frameSize);
            
            auto frame = make<WebPFrame>(frameBuffer, timestamp - prevTimestamp);
            frames.push_back(frame);

            prevTimestamp = timestamp;
            frameIndex++;
        }

        WebPAnimDecoderDelete(decoder);

        auto framesVector = single_threaded_vector<NativeMediaWinRT::WebPFrame>(std::move(frames));
        return make<WebPAnimation>(
            static_cast<int32_t>(animInfo.canvas_width),
            static_cast<int32_t>(animInfo.canvas_height),
            static_cast<int32_t>(animInfo.frame_count),
            static_cast<int32_t>(animInfo.loop_count),
            framesVector.GetView()
        );
    }
}
