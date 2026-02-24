#include "framework.h"
#include "NativeMedia.h"
#include <fstream>
#include <vector>
#include <webp/decode.h>
#include <webp/demux.h>
#include <iostream>

extern "C" WebPAnimation* DecodeWebPAnimation(const wchar_t* filePath) {
    std::ifstream file(filePath, std::ios::binary | std::ios::ate);
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

    WebPAnimation* animation = new WebPAnimation();
    animation->width = animInfo.canvas_width;
    animation->height = animInfo.canvas_height;
    animation->frameCount = animInfo.frame_count;
    animation->loopCount = animInfo.loop_count;
    animation->frames = new WebPFrame[animInfo.frame_count];

    int frameIndex = 0;
    uint8_t* buf;
    int timestamp = 0;
    int prevTimestamp = 0;

    while (WebPAnimDecoderGetNext(decoder, &buf, &timestamp)) {
        if (frameIndex >= animInfo.frame_count) break;

        size_t frameSize = static_cast<size_t>(animation->width) * animation->height * 4;
        animation->frames[frameIndex].bgraBuffer = new uint8_t[frameSize];
        memcpy(animation->frames[frameIndex].bgraBuffer, buf, frameSize);
        animation->frames[frameIndex].durationMs = timestamp - prevTimestamp;

        prevTimestamp = timestamp;
        frameIndex++;
    }

    WebPAnimDecoderDelete(decoder);
    return animation;
}

extern "C" void FreeWebPAnimation(WebPAnimation* animation) {
    if (!animation) return;
    if (animation->frames) {
        for (int i = 0; i < animation->frameCount; ++i) {
            delete[] animation->frames[i].bgraBuffer;
        }
        delete[] animation->frames;
    }
    delete animation;
}

