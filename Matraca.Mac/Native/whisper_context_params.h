#ifndef MATRACA_WHISPER_CONTEXT_PARAMS_H
#define MATRACA_WHISPER_CONTEXT_PARAMS_H

#include "whisper_aheads.h"
#include "whisper_alignment_heads_preset.h"

#include <stdbool.h>
#include <stddef.h>

struct whisper_context_params {
    bool use_gpu;
    bool flash_attn;
    int gpu_device;
    bool dtw_token_timestamps;
    enum whisper_alignment_heads_preset dtw_aheads_preset;
    int dtw_n_top;
    struct whisper_aheads dtw_aheads;
    size_t dtw_mem_size;
};

#endif
