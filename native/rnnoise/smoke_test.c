#include <math.h>
#include <stdio.h>

#include "rnnoise.h"

int main(void)
{
    float input[480] = {0};
    float output[480] = {0};
    float vad_probability;
    DenoiseState *state;
    int i;

    if (rnnoise_get_frame_size() != 480) {
        fprintf(stderr, "Unexpected RNNoise frame size: %d\n", rnnoise_get_frame_size());
        return 10;
    }

    if (rnnoise_get_size() <= 0) {
        fputs("RNNoise returned an invalid state size.\n", stderr);
        return 11;
    }

    state = rnnoise_create(NULL);
    if (state == NULL) {
        fputs("rnnoise_create(NULL) failed.\n", stderr);
        return 12;
    }

    vad_probability = rnnoise_process_frame(state, output, input);
    if (!isfinite(vad_probability)) {
        fputs("RNNoise returned a non-finite VAD probability.\n", stderr);
        rnnoise_destroy(state);
        return 13;
    }

    for (i = 0; i < 480; ++i) {
        if (!isfinite(output[i])) {
            fprintf(stderr, "RNNoise returned a non-finite sample at index %d.\n", i);
            rnnoise_destroy(state);
            return 14;
        }
    }

    rnnoise_destroy(state);
    puts("RNNoise smoke test passed (frame size 480, full model initialized).");
    return 0;
}
