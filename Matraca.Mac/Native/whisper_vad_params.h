#ifndef MATRACA_WHISPER_VAD_PARAMS_H
#define MATRACA_WHISPER_VAD_PARAMS_H

struct whisper_vad_params {
    float threshold;
    int min_speech_duration_ms;
    int min_silence_duration_ms;
    float max_speech_duration_s;
    int speech_pad_ms;
    float samples_overlap;
};

#endif
