#include <dlfcn.h>
#include <pthread.h>
#include <stdarg.h>
#include <stdatomic.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "whisper_context_params.h"
#include "whisper_full_params.h"

// ABI subset pinned to whisper.cpp f24588a, shipped by Whisper.net.Runtime 1.9.1.
struct whisper_context;
struct whisper_state;

typedef void (*matraca_log_fn)(int level, const char *message);
typedef struct whisper_context_params *(*default_context_params_fn)(void);
typedef struct whisper_context *(*init_context_fn)(const char *model_path, struct whisper_context_params params);
typedef struct whisper_state *(*init_state_fn)(struct whisper_context *context);
typedef struct whisper_full_params *(*default_params_fn)(enum whisper_sampling_strategy strategy);
typedef int (*full_fn)(struct whisper_context *, struct whisper_state *, struct whisper_full_params, const float *, int);
typedef int (*segment_count_fn)(struct whisper_state *);
typedef const char *(*segment_text_fn)(struct whisper_state *, int);
typedef void (*free_state_fn)(struct whisper_state *);
typedef void (*free_context_fn)(struct whisper_context *);
typedef void (*free_params_fn)(struct whisper_full_params *);
typedef void (*free_context_params_fn)(struct whisper_context_params *);
typedef void (*set_log_fn)(void (*)(int, const char *, void *), void *);
static _Atomic(matraca_log_fn) active_log;

struct matraca_whisper {
    void *libraries[6];
    struct whisper_context *context;
    struct whisper_state *state;
    struct whisper_full_params *params;
    char *language;
    char *prompt;
    matraca_log_fn log;
    pthread_mutex_t mutex;
    atomic_bool abort_requested;
    uint64_t run_count;
    default_context_params_fn default_context_params;
    init_context_fn init_context;
    init_state_fn init_state;
    default_params_fn default_params;
    full_fn full;
    segment_count_fn segment_count;
    segment_text_fn segment_text;
    free_state_fn free_state;
    free_context_fn free_context;
    free_params_fn free_params;
    free_context_params_fn free_context_params;
    set_log_fn set_log;
};

static void set_error(char **out_error, const char *format, ...) {
    if (out_error == NULL) return;
    char buffer[1024];
    va_list args;
    va_start(args, format);
    vsnprintf(buffer, sizeof(buffer), format, args);
    va_end(args);
    *out_error = strdup(buffer);
}

static void native_log(int level, const char *message, void *user_data) {
    (void) user_data;
    matraca_log_fn log = atomic_load(&active_log);
    if (log != NULL) log(level, message);
}

static bool should_abort(void *user_data) {
    struct matraca_whisper *session = user_data;
    return session != NULL && atomic_load(&session->abort_requested);
}

static void close_libraries(struct matraca_whisper *session) {
    for (int index = 5; index >= 0; index--) {
        if (session->libraries[index] != NULL) dlclose(session->libraries[index]);
    }
}

static bool load_library(struct matraca_whisper *session, const char *directory, int index, const char *name, char **error) {
    size_t length = strlen(directory) + strlen(name) + 2;
    char *path = malloc(length);
    if (path == NULL) {
        set_error(error, "Sem memoria ao montar caminho da biblioteca nativa.");
        return false;
    }
    snprintf(path, length, "%s/%s", directory, name);
    session->libraries[index] = dlopen(path, RTLD_NOW | RTLD_GLOBAL);
    free(path);
    if (session->libraries[index] != NULL) return true;
    set_error(error, "Falha ao carregar %s: %s", name, dlerror());
    return false;
}

static bool load_symbols(struct matraca_whisper *session, char **error) {
    void *library = session->libraries[5];
#define LOAD(field, symbol) do { \
    *(void **)(&session->field) = dlsym(library, symbol); \
    if (session->field == NULL) { set_error(error, "Simbolo ausente: %s", symbol); return false; } \
} while (0)
    LOAD(default_context_params, "whisper_context_default_params_by_ref");
    LOAD(init_context, "whisper_init_from_file_with_params_no_state");
    LOAD(init_state, "whisper_init_state");
    LOAD(default_params, "whisper_full_default_params_by_ref");
    LOAD(full, "whisper_full_with_state");
    LOAD(segment_count, "whisper_full_n_segments_from_state");
    LOAD(segment_text, "whisper_full_get_segment_text_from_state");
    LOAD(free_state, "whisper_free_state");
    LOAD(free_context, "whisper_free");
    LOAD(free_params, "whisper_free_params");
    LOAD(free_context_params, "whisper_free_context_params");
    LOAD(set_log, "whisper_log_set");
#undef LOAD
    return true;
}

__attribute__((visibility("default")))
int matraca_whisper_open(
    const char *runtime_directory,
    const char *model_path,
    const char *language,
    const char *prompt,
    int use_gpu,
    matraca_log_fn log,
    struct matraca_whisper **out_session,
    char **out_error) {
    if (out_session == NULL) return 1;
    *out_session = NULL;
    if (out_error != NULL) *out_error = NULL;

    struct matraca_whisper *session = calloc(1, sizeof(*session));
    if (session == NULL) {
        set_error(out_error, "Sem memoria ao criar sessao Whisper.");
        return 1;
    }
    session->log = log;
    atomic_store(&active_log, log);
    int mutex_result = pthread_mutex_init(&session->mutex, NULL);
    if (mutex_result != 0) {
        set_error(out_error, "Falha ao criar mutex da sessao Whisper (codigo %d).", mutex_result);
        free(session);
        return 1;
    }
    atomic_init(&session->abort_requested, false);

    const char *libraries[] = {
        "libggml-base-whisper.dylib",
        "libggml-cpu-whisper.dylib",
        "libggml-blas-whisper.dylib",
        "libggml-metal-whisper.dylib",
        "libggml-whisper.dylib",
        "libwhisper.dylib",
    };
    for (int index = 0; index < 6; index++) {
        if (!load_library(session, runtime_directory, index, libraries[index], out_error)) goto fail;
    }
    if (!load_symbols(session, out_error)) goto fail;
    session->set_log(native_log, NULL);

    session->language = strdup(language == NULL || language[0] == '\0' ? "pt" : language);
    session->prompt = strdup(prompt == NULL ? "" : prompt);
    if (session->language == NULL || session->prompt == NULL) {
        set_error(out_error, "Sem memoria ao configurar sessao Whisper.");
        goto fail;
    }

    struct whisper_context_params *context_params = session->default_context_params();
    if (context_params == NULL) {
        set_error(out_error, "Whisper nao conseguiu criar parametros de contexto.");
        goto fail;
    }
    context_params->use_gpu = use_gpu != 0;
    session->context = session->init_context(model_path, *context_params);
    session->free_context_params(context_params);
    if (session->context == NULL) {
        set_error(out_error, "Whisper nao conseguiu carregar o modelo: %s", model_path);
        goto fail;
    }
    session->state = session->init_state(session->context);
    if (session->state == NULL) {
        set_error(out_error, "Whisper nao conseguiu criar o estado persistente.");
        goto fail;
    }
    session->params = session->default_params(WHISPER_SAMPLING_GREEDY);
    if (session->params == NULL) {
        set_error(out_error, "Whisper nao conseguiu criar parametros de transcricao.");
        goto fail;
    }
    session->params->language = session->language;
    session->params->initial_prompt = session->prompt[0] == '\0' ? NULL : session->prompt;
    session->params->no_context = true;
    session->params->print_progress = false;
    session->params->print_realtime = false;
    session->params->print_timestamps = false;
    session->params->abort_callback = should_abort;
    session->params->abort_callback_user_data = session;
    session->params->greedy.best_of = 1;

    if (session->log != NULL) session->log(2, "matraca_whisper: estado persistente pronto\n");
    *out_session = session;
    return 0;

fail:
    if (session->params != NULL && session->free_params != NULL) session->free_params(session->params);
    if (session->state != NULL && session->free_state != NULL) session->free_state(session->state);
    if (session->context != NULL && session->free_context != NULL) session->free_context(session->context);
    free(session->language);
    free(session->prompt);
    close_libraries(session);
    pthread_mutex_destroy(&session->mutex);
    free(session);
    return 1;
}

__attribute__((visibility("default")))
int matraca_whisper_transcribe(
    struct matraca_whisper *session,
    const float *samples,
    int sample_count,
    char **out_text,
    char **out_error) {
    if (out_text == NULL || session == NULL) return 1;
    *out_text = NULL;
    if (out_error != NULL) *out_error = NULL;
    pthread_mutex_lock(&session->mutex);

    if (session->state == NULL) {
        set_error(out_error, "Estado Whisper indisponivel apos falha nativa.");
        pthread_mutex_unlock(&session->mutex);
        return 1;
    }

    int result = session->full(session->context, session->state, *session->params, samples, sample_count);
    if (result != 0) {
        set_error(out_error, "Whisper falhou ao transcrever (codigo %d).", result);
        if (result == -7) {
            // whisper.cpp frees the state itself when decoder allocation fails.
            session->state = session->init_state(session->context);
        }
        pthread_mutex_unlock(&session->mutex);
        return result;
    }

    int count = session->segment_count(session->state);
    size_t total = 1;
    for (int index = 0; index < count; index++) {
        const char *text = session->segment_text(session->state, index);
        if (text != NULL) total += strlen(text);
    }
    char *text = calloc(total, 1);
    if (text == NULL) {
        set_error(out_error, "Sem memoria ao copiar a transcricao.");
        pthread_mutex_unlock(&session->mutex);
        return 1;
    }
    for (int index = 0; index < count; index++) {
        const char *segment = session->segment_text(session->state, index);
        if (segment != NULL) strcat(text, segment);
    }
    session->run_count++;
    *out_text = text;
    pthread_mutex_unlock(&session->mutex);
    return 0;
}

__attribute__((visibility("default")))
void matraca_whisper_reset_cancel(struct matraca_whisper *session) {
    if (session != NULL) atomic_store(&session->abort_requested, false);
}

__attribute__((visibility("default")))
void matraca_whisper_cancel(struct matraca_whisper *session) {
    if (session != NULL) atomic_store(&session->abort_requested, true);
}

__attribute__((visibility("default")))
uint64_t matraca_whisper_run_count(struct matraca_whisper *session) {
    return session == NULL ? 0 : session->run_count;
}

__attribute__((visibility("default")))
void matraca_whisper_string_free(char *value) {
    free(value);
}

__attribute__((visibility("default")))
void matraca_whisper_close(struct matraca_whisper *session) {
    if (session == NULL) return;
    pthread_mutex_lock(&session->mutex);
    if (session->log != NULL) session->log(2, "matraca_whisper: liberando estado persistente\n");
    session->free_params(session->params);
    if (session->state != NULL) session->free_state(session->state);
    session->free_context(session->context);
    free(session->language);
    free(session->prompt);
    pthread_mutex_unlock(&session->mutex);
    pthread_mutex_destroy(&session->mutex);
    close_libraries(session);
    free(session);
}
