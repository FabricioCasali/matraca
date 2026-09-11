import Foundation
import CryptoKit
import Darwin

// Only this short-lived process owns the test seed. No Keychain, env, argv or key file.
// stdout protocol: public key, then one public signature per archive path read on stdin.
// Sparkle 2.8.0 common_cli/secret.swift accepts the 32-byte seed via --ed-key-file -.
let key = Curve25519.Signing.PrivateKey()
let tool = URL(fileURLWithPath: CommandLine.arguments[1])
print(key.publicKey.rawRepresentation.base64EncodedString())
fflush(stdout)
while let path = readLine() {
    do {
        let task = Process()
        let input = Pipe()
        let output = Pipe()
        task.executableURL = tool
        task.arguments = ["--ed-key-file", "-", "-p", path]
        task.standardInput = input
        task.standardOutput = output
        // sign_update can echo malformed input on failure: never forward its stderr.
        task.standardError = FileHandle.nullDevice
        try task.run()
        try input.fileHandleForWriting.write(contentsOf:
            Data((key.rawRepresentation.base64EncodedString() + "\n").utf8))
        try input.fileHandleForWriting.close()
        let result = output.fileHandleForReading.readDataToEndOfFile()
        task.waitUntilExit()
        guard task.terminationStatus == 0,
              let text = String(data: result, encoding: .utf8),
              let signature = Data(base64Encoded: text.trimmingCharacters(in: .whitespacesAndNewlines)),
              signature.count == 64 else { exit(1) }
        let archive = try Data(contentsOf: URL(fileURLWithPath: path), options: .mappedIfSafe)
        guard key.publicKey.isValidSignature(signature, for: archive) else { exit(1) }
        print(signature.base64EncodedString())
        fflush(stdout)
    } catch {
        fputs("Falha no assinador efêmero de TESTE.\n", stderr)
        exit(1)
    }
}
