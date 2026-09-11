#import <AppKit/AppKit.h>
#import <Sparkle/Sparkle.h>
#include <unistd.h>

typedef int (*MPGate)(int operation);

@interface ProbeDriver : NSObject <NSApplicationDelegate, SPUUserDriver, SPUUpdaterDelegate, NSURLSessionTaskDelegate>
@property(nonatomic) MPGate gate;
@property(nonatomic, strong) SPUUpdater *updater;
@property(nonatomic, strong) NSStatusItem *status;
@property(nonatomic, strong) NSURLSession *session;
@property(nonatomic, strong) NSURLSessionDownloadTask *prefetch;
@property(nonatomic, copy) void (^foundReply)(SPUUserUpdateChoice);
@property(nonatomic, copy) NSString *root;
@property(nonatomic, copy) NSString *version;
@property(nonatomic, copy) NSString *feed;
@property(nonatomic) NSUInteger generation;
@property(nonatomic) BOOL downloaded;
@property(nonatomic) BOOL confirmed;
@end

@implementation ProbeDriver
- (void)report:(NSString *)event {
    NSAssert([NSThread isMainThread], @"AppKit/Sparkle must run on main thread");
    event = [event stringByReplacingOccurrencesOfString:@"\n" withString:@" "];
    self.status.button.toolTip = event;
    NSMenuItem *line = [self.status.menu itemAtIndex:0];
    line.title = event;
    NSString *log = [self.root stringByAppendingPathComponent:@"events.log"];
    FILE *file = fopen(log.fileSystemRepresentation, "a");
    if (file) {
        fprintf(file, "%.3f pid=%d v=%s %s\n", NSDate.date.timeIntervalSince1970,
                getpid(), self.version.UTF8String, event.UTF8String);
        fclose(file);
    }
}
- (void)reportError:(NSError *)error {
    // Preserve the underlying signature failure, not only a generic updater code.
    for (NSUInteger depth = 0; error != nil && depth < 4; depth++) {
        [self report:[NSString stringWithFormat:@"error: %@/%ld %@", error.domain,
                      (long)error.code, error.localizedDescription]];
        error = error.userInfo[NSUnderlyingErrorKey];
    }
}
- (void)addAction:(NSString *)title selector:(SEL)selector menu:(NSMenu *)menu {
    NSMenuItem *item = [[NSMenuItem alloc] initWithTitle:title action:selector keyEquivalent:@""];
    item.target = self;
    [menu addItem:item];
}
- (void)applicationDidFinishLaunching:(NSNotification *)notification {
    self.status = [[NSStatusBar systemStatusBar] statusItemWithLength:NSVariableStatusItemLength];
    self.status.button.title = [@"MT038 v" stringByAppendingString:self.version];
    NSMenu *menu = [NSMenu new];
    [menu addItemWithTitle:@"Iniciando" action:NULL keyEquivalent:@""];
    [self addAction:@"Consultar novamente" selector:@selector(check:) menu:menu];
    [self addAction:@"1. Simular captura" selector:@selector(begin:) menu:menu];
    [self addAction:@"2. Parar captura (entrega pendente)" selector:@selector(stop:) menu:menu];
    [self addAction:@"3. Concluir entrega simulada" selector:@selector(drain:) menu:menu];
    [self addAction:@"Confirmar instalação…" selector:@selector(confirm:) menu:menu];
    [self addAction:@"Rejeitar esta oferta" selector:@selector(reject:) menu:menu];
    [self addAction:@"Sair do probe" selector:@selector(quit:) menu:menu];
    self.status.menu = menu;
    [self report:[NSString stringWithFormat:@"launch: .NET -> shim -> AppKit; managed-major=%d", self.gate(7)]];
    self.session = [NSURLSession sessionWithConfiguration:NSURLSessionConfiguration.ephemeralSessionConfiguration
                                                delegate:self delegateQueue:nil];
    NSBundle *bundle = NSBundle.mainBundle;
    self.updater = [[SPUUpdater alloc] initWithHostBundle:bundle applicationBundle:bundle userDriver:self delegate:self];
    NSError *error = nil;
    if (![self.updater startUpdater:&error]) {
        [self reportError:error];
        return;
    }
    // Documented immediate launch check, before the next runloop cycle.
    if (self.updater.automaticallyChecksForUpdates) [self.updater checkForUpdatesInBackground];
}
- (void)check:(id)sender {
    if (self.updater.canCheckForUpdates) [self.updater checkForUpdates];
}
- (void)begin:(id)sender { [self report:self.gate(1) ? @"captura simulada ativa" : @"captura bloqueada"]; }
- (void)stop:(id)sender { [self report:self.gate(2) ? @"captura parada; entrega pendente" : @"nenhuma captura para parar"]; }
- (void)drain:(id)sender { [self report:self.gate(3) ? @"entrega simulada concluída" : @"drenagem recusada"]; }
- (void)clearPrefetch {
    self.generation++;
    [self.prefetch cancel];
    self.prefetch = nil;
    self.downloaded = NO;
    [[NSFileManager defaultManager] removeItemAtPath:[self.root stringByAppendingPathComponent:@"prefetch.zip"] error:NULL];
}
- (void)reject:(id)sender {
    if (self.confirmed) { [self report:@"instalação já confirmada; aguarde conclusão"]; return; }
    [self clearPrefetch];
    void (^reply)(SPUUserUpdateChoice) = self.foundReply;
    self.foundReply = nil;
    // Only held at NotDownloaded. Dismiss at Installing would install on quit!
    if (reply) reply(SPUUserUpdateChoiceDismiss);
    [self report:@"oferta rejeitada; nenhum instalador autorizado"];
}
- (void)confirm:(id)sender {
    if (!self.foundReply || !self.downloaded || self.confirmed) {
        [self report:@"confirmação indisponível; aguarde download antecipado"]; return;
    }
    if (!self.gate(0)) { [self report:@"confirmação bloqueada: captura/entrega ativa"]; return; }
    NSUInteger generation = self.generation;
    NSAlert *alert = [NSAlert new];
    alert.messageText = @"Instalar o pacote de TESTE e reiniciar o probe?";
    alert.informativeText = @"Somente este bundle isolado. Sparkle baixará novamente e validará a assinatura antes de extrair. Esta confirmação autoriza também instalar ao sair neste ciclo.";
    [alert addButtonWithTitle:@"Cancelar"];
    [alert addButtonWithTitle:@"Confirmar instalação"];
    if ([alert runModal] != NSAlertSecondButtonReturn) { [self report:@"confirmação cancelada"]; return; }
    // Revalidate after nested AppKit loop; never reuse a stale offer/lease.
    if (generation != self.generation || !self.foundReply || !self.gate(4)) {
        [self report:@"confirmação invalidada"]; return;
    }
    self.confirmed = YES;
    void (^reply)(SPUUserUpdateChoice) = self.foundReply;
    self.foundReply = nil;
    [self clearPrefetch];
    [self report:@"install-confirmed: gate adquirido; novas capturas bloqueadas"];
    reply(SPUUserUpdateChoiceInstall);
}
- (void)quit:(id)sender { [NSApp terminate:nil]; }
- (NSApplicationTerminateReply)applicationShouldTerminate:(NSApplication *)sender {
    if (!self.gate(6)) { [self report:@"quit bloqueado: captura/entrega pendente"]; return NSTerminateCancel; }
    if (!self.confirmed) [self reject:nil];
    [self report:@"terminate: gate livre"];
    return NSTerminateNow;
}
- (NSString *)feedURLStringForUpdater:(SPUUpdater *)updater { return self.feed; }
- (BOOL)updater:(SPUUpdater *)updater shouldProceedWithUpdate:(SUAppcastItem *)item
    updateCheck:(SPUUpdateCheck)check error:(NSError *__autoreleasing *)error {
    NSURL *expected = [[NSURL URLWithString:self.feed] URLByDeletingLastPathComponent];
    expected = [expected URLByAppendingPathComponent:@"update.zip"];
    BOOL allowed = !item.informationOnlyUpdate && [item.installationType isEqualToString:@"application"]
        && [item.fileURL isEqual:expected] && [item.versionString isEqualToString:@"2.0"];
    if (!allowed && error) *error = [NSError errorWithDomain:@"MacUpdateProbe" code:1
        userInfo:@{NSLocalizedDescriptionKey: @"Feed fora do contrato isolado do probe."}];
    return allowed;
}
- (BOOL)updater:(SPUUpdater *)updater shouldDownloadReleaseNotesForUpdate:(SUAppcastItem *)item { return NO; }
- (NSArray<NSString *> *)allowedSystemProfileKeysForUpdater:(SPUUpdater *)updater { return @[]; }
- (void)updater:(SPUUpdater *)updater didAbortWithError:(NSError *)error {
    [self report:@"abort: ciclo interrompido"];
    [self reportError:error];
}
- (void)showUpdateFoundWithAppcastItem:(SUAppcastItem *)item state:(SPUUserUpdateState *)state
    reply:(void (^)(SPUUserUpdateChoice))reply {
    if (state.stage != SPUUserUpdateStageNotDownloaded || item.informationOnlyUpdate) {
        [self report:@"estado inesperado: cancelar, nunca instalar ao sair"];
        reply(SPUUserUpdateChoiceSkip);
        return;
    }
    self.confirmed = NO;
    self.foundReply = reply;
    [self clearPrefetch];
    NSUInteger generation = self.generation;
    [self report:@"oferta encontrada; download antecipado automático (ainda não validado)"];
    self.prefetch = [self.session downloadTaskWithURL:item.fileURL completionHandler:
        ^(NSURL *location, NSURLResponse *response, NSError *error) {
            // Completion is not on main. Keep temp URL alive until move finishes.
            dispatch_sync(dispatch_get_main_queue(), ^{
                if (generation != self.generation || !self.foundReply) return;
                NSError *moveError = nil;
                BOOL ok = !error && [(NSHTTPURLResponse *)response statusCode] == 200 && location != nil;
                if (ok) ok = [[NSFileManager defaultManager] moveItemAtURL:location toURL:
                    [NSURL fileURLWithPath:[self.root stringByAppendingPathComponent:@"prefetch.zip"]] error:&moveError];
                self.downloaded = ok;
                [self report:ok ? @"download antecipado concluído; aguardando confirmação (não validado)"
                                : @"download antecipado falhou; rejeite e consulte novamente"];
            });
        }];
    [self.prefetch resume];
}
- (void)URLSession:(NSURLSession *)session task:(NSURLSessionTask *)task
    willPerformHTTPRedirection:(NSHTTPURLResponse *)response newRequest:(NSURLRequest *)request
    completionHandler:(void (^)(NSURLRequest *))completionHandler { completionHandler(nil); }
- (void)showUpdatePermissionRequest:(SPUUpdatePermissionRequest *)request reply:(void (^)(SUUpdatePermissionResponse *))reply {
    reply([[SUUpdatePermissionResponse alloc] initWithAutomaticUpdateChecks:NO sendSystemProfile:NO]);
}
- (void)showUserInitiatedUpdateCheckWithCancellation:(void (^)(void))cancel { [self report:@"consultando feed local"]; }
- (void)showUpdateReleaseNotesWithDownloadData:(SPUDownloadData *)data { }
- (void)showUpdateReleaseNotesFailedToDownloadWithError:(NSError *)error { [self report:@"notas indisponíveis"]; }
- (void)showUpdateNotFoundWithError:(NSError *)error acknowledgement:(void (^)(void))ack {
    [self report:@"nenhuma atualização aplicável"]; ack();
}
- (void)showUpdaterError:(NSError *)error acknowledgement:(void (^)(void))ack {
    [self report:@"update-error: ciclo falhou"];
    [self reportError:error];
    ack();
}
- (void)showDownloadInitiatedWithCancellation:(void (^)(void))cancel {
    if (!self.confirmed) { cancel(); return; }
    [self report:@"Sparkle: download autorizado"];
}
- (void)showDownloadDidReceiveExpectedContentLength:(uint64_t)length { }
- (void)showDownloadDidReceiveDataOfLength:(uint64_t)length { }
- (void)showDownloadDidStartExtractingUpdate { [self report:@"Sparkle: etapa de validação/extração iniciada (não é prova de assinatura)"]; }
- (void)showExtractionReceivedProgress:(double)progress { }
- (void)showReadyToInstallAndRelaunch:(void (^)(SPUUserUpdateChoice))reply {
    BOOL allowed = self.confirmed && self.gate(6);
    [self report:allowed ? @"Sparkle: instalar e relançar autorizado" : @"Sparkle: instalação cancelada pelo gate"];
    reply(allowed ? SPUUserUpdateChoiceInstall : SPUUserUpdateChoiceSkip);
}
- (void)showInstallingUpdateWithApplicationTerminated:(BOOL)terminated retryTerminatingApplication:(void (^)(void))retry {
    [self report:@"Sparkle: instalando; aguardando término do host"];
}
- (void)showUpdateInstalledAndRelaunched:(BOOL)relaunched acknowledgement:(void (^)(void))ack {
    [self report:relaunched ? @"Sparkle: informou relaunch" : @"Sparkle: informou instalação sem relaunch"]; ack();
}
- (void)dismissUpdateInstallation {
    [self clearPrefetch];
    self.foundReply = nil;
    self.confirmed = NO;
    self.gate(5);
    // Do not replace the error/status with a generic success message.
}
@end

__attribute__((visibility("default"))) int mp_run(MPGate gate) {
    @autoreleasepool {
        if (![NSThread isMainThread] || !gate) return 2;
        NSBundle *bundle = NSBundle.mainBundle;
        NSString *root = [bundle objectForInfoDictionaryKey:@"MPProbeRoot"];
        NSString *identifier = bundle.bundleIdentifier;
        NSString *expected = [root stringByAppendingPathComponent:@"installed/MacUpdateProbe.app"];
        if (![identifier hasPrefix:@"io.github.fabriciocasali.matraca.updateprobe.r"] || !root.length
            || ![[bundle.bundlePath stringByResolvingSymlinksInPath] isEqualToString:[expected stringByResolvingSymlinksInPath]]) {
            fputs("Bundle fora da instalação isolada do probe.\n", stderr); return 2;
        }
        [NSApplication sharedApplication];
        [NSApp setActivationPolicy:NSApplicationActivationPolicyAccessory];
        __attribute__((objc_precise_lifetime)) ProbeDriver *driver = [ProbeDriver new];
        driver.gate = gate;
        driver.root = root;
        driver.version = [bundle objectForInfoDictionaryKey:@"CFBundleVersion"];
        driver.feed = [bundle objectForInfoDictionaryKey:@"SUFeedURL"];
        NSApp.delegate = driver;
        [NSApp run];
        [driver.session invalidateAndCancel];
        return 0;
    }
}
