#ifndef MATRACA_WHISPER_AHEADS_H
#define MATRACA_WHISPER_AHEADS_H

#include <stddef.h>

struct whisper_aheads {
    size_t n_heads;
    const void *heads;
};

#endif
