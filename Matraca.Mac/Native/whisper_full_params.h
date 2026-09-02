#ifndef MATRACA_WHISPER_FULL_PARAMS_H
#define MATRACA_WHISPER_FULL_PARAMS_H

#include "whisper_sampling_strategy.h"
#include "whisper_vad_params.h"
#include "whisper_greedy_params.h"
#include "whisper_beam_search_params.h"

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

typedef int32_t whisper_token;

struct whisper_full_params {
    enum whisper_sampling_strategy strategy;
    int n_threads;
    int n_max_text_ctx;
    int offset_ms;
    int duration_ms;
    bool translate;
    bool no_context;
    bool no_timestamps;
    bool single_segment;
    bool print_special;
    bool print_progress;
    bool print_realtime;
    bool print_timestamps;
    bool token_timestamps;
    float thold_pt;
    float thold_ptsum;
    int max_len;
    bool split_on_word;
    int max_tokens;
    bool debug_mode;
    int audio_ctx;
    bool tdrz_enable;
    const char *suppress_regex;
    const char *initial_prompt;
    bool carry_initial_prompt;
    const whisper_token *prompt_tokens;
    int prompt_n_tokens;
    const char *language;
    bool detect_language;
    bool suppress_blank;
    bool suppress_nst;
    float temperature;
    float max_initial_ts;
    float length_penalty;
    float temperature_inc;
    float entropy_thold;
    float logprob_thold;
    float no_speech_thold;
    struct whisper_greedy_params greedy;
    struct whisper_beam_search_params beam_search;
    void *new_segment_callback;
    void *new_segment_callback_user_data;
    void *progress_callback;
    void *progress_callback_user_data;
    void *encoder_begin_callback;
    void *encoder_begin_callback_user_data;
    bool (*abort_callback)(void *user_data);
    void *abort_callback_user_data;
    void *logits_filter_callback;
    void *logits_filter_callback_user_data;
    const void **grammar_rules;
    size_t n_grammar_rules;
    size_t i_start_rule;
    float grammar_penalty;
    bool vad;
    const char *vad_model_path;
    struct whisper_vad_params vad_params;
};

#endif
