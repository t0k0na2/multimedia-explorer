#pragma once
#include <stdint.h>

extern "C" {
    struct WebPFrame {
        uint8_t* bgraBuffer;
        int durationMs;
    };

    struct WebPAnimation {
        int width;
        int height;
        int frameCount;
        int loopCount;
        WebPFrame* frames;
    };

    __declspec(dllexport) WebPAnimation* DecodeWebPAnimation(const wchar_t* filePath);
    __declspec(dllexport) void FreeWebPAnimation(WebPAnimation* animation);
}
