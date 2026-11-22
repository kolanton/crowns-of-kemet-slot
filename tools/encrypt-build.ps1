<#
.SYNOPSIS
    Encrypts the WebGL build so a public static host can serve it without serving the game.

.DESCRIPTION
    A static host has no server to check a password against, so the password has to BE
    the key. Each build file is encrypted with AES-256-CBC under a key derived from the
    password with PBKDF2, then authenticated with HMAC-SHA256 over the IV and ciphertext
    together. What the host holds is noise without the password, so the gate is not a
    prompt that a determined visitor can simply skip past: there is nothing behind it to
    reach.

    The loader is copied in clear. It is Unity boilerplate, identical in every WebGL
    build, and carries none of this project's content.

.EXAMPLE
    pwsh -File tools/encrypt-build.ps1
    Prompts for the password twice and writes docs/Build.
#>
param(
    # Unity's WebGL output, the folder holding WebGL.data.unityweb and friends.
    [string] $Source = 'Build/WebGL/Build',

    # Where the encrypted build goes. Published as-is.
    [string] $Out = 'docs/Build',

    # Skips the prompt. Convenient for testing; it puts the password in your shell
    # history, so do not use it with the real one.
    [string] $Password,

    # OWASP's 2023 floor for PBKDF2-HMAC-SHA256. Costs a phone about a second.
    [int] $Iterations = 310000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root      = Split-Path -Parent $PSScriptRoot
$sourceDir = Join-Path $root $Source
$outDir    = Join-Path $root $Out

if (-not (Test-Path -LiteralPath $sourceDir)) {
    throw "No build at $sourceDir. Run the Unity WebGL build first."
}

function Read-Secret {
    $first  = Read-Host -Prompt 'Password for the demo' -AsSecureString
    $second = Read-Host -Prompt 'Type it again' -AsSecureString

    $a = [Runtime.InteropServices.Marshal]::PtrToStringBSTR(
            [Runtime.InteropServices.Marshal]::SecureStringToBSTR($first))
    $b = [Runtime.InteropServices.Marshal]::PtrToStringBSTR(
            [Runtime.InteropServices.Marshal]::SecureStringToBSTR($second))

    if ($a -cne $b)    { throw 'The two passwords differ.' }
    if ($a.Length -lt 8) { throw 'Use at least eight characters.' }
    return $a
}

$secret = if ($Password) { $Password } else { Read-Secret }

$rng  = [Security.Cryptography.RandomNumberGenerator]::Create()
$salt = New-Object byte[] 16
$rng.GetBytes($salt)

# One derivation, split in two: the halves are independent, so the MAC key never doubles
# as the encryption key.
$kdf = New-Object Security.Cryptography.Rfc2898DeriveBytes(
    $secret, $salt, $Iterations, [Security.Cryptography.HashAlgorithmName]::SHA256)
$material = $kdf.GetBytes(64)
$aesKey   = [byte[]] $material[0..31]
$macKey   = [byte[]] $material[32..63]

if (-not (Test-Path -LiteralPath $outDir)) {
    New-Item -ItemType Directory -Path $outDir | Out-Null
}

# A leftover plaintext build would serve the game next to the locked one.
Get-ChildItem -LiteralPath $outDir -Filter '*.unityweb' -ErrorAction SilentlyContinue |
    Remove-Item -Force

$roles = [ordered]@{
    data      = 'WebGL.data.unityweb'
    framework = 'WebGL.framework.js.unityweb'
    code      = 'WebGL.wasm.unityweb'
}

$files = [ordered]@{}

foreach ($role in $roles.Keys) {
    $name  = $roles[$role]
    $plain = [IO.File]::ReadAllBytes((Join-Path $sourceDir $name))

    $iv = New-Object byte[] 16
    $rng.GetBytes($iv)

    $aes = [Security.Cryptography.Aes]::Create()
    $aes.Mode    = [Security.Cryptography.CipherMode]::CBC
    $aes.Padding = [Security.Cryptography.PaddingMode]::PKCS7
    $aes.Key     = $aesKey
    $aes.IV      = $iv
    $cipher = $aes.CreateEncryptor().TransformFinalBlock($plain, 0, $plain.Length)
    $aes.Dispose()

    # Encrypt-then-MAC. The tag covers the IV too, so one file's header cannot be
    # grafted onto another's body.
    $signed = New-Object byte[] (16 + $cipher.Length)
    [Array]::Copy($iv, 0, $signed, 0, 16)
    [Array]::Copy($cipher, 0, $signed, 16, $cipher.Length)

    $hmac = New-Object Security.Cryptography.HMACSHA256(, $macKey)
    $tag  = $hmac.ComputeHash($signed)
    $hmac.Dispose()

    # iv || ciphertext || tag, so the browser verifies one contiguous slice.
    $blob = New-Object byte[] ($signed.Length + 32)
    [Array]::Copy($signed, 0, $blob, 0, $signed.Length)
    [Array]::Copy($tag, 0, $blob, $signed.Length, 32)

    $encName = [IO.Path]::GetFileNameWithoutExtension($name) + '.enc'
    [IO.File]::WriteAllBytes((Join-Path $outDir $encName), $blob)

    $files[$role] = [ordered]@{ path = $encName; bytes = $blob.Length }

    '{0,-28} {1,10:N0} -> {2,10:N0} bytes' -f $name, $plain.Length, $blob.Length
}

# Lets the page reject a wrong password in a few hundred milliseconds instead of after a
# fifteen megabyte download. It reveals nothing the encrypted files do not already.
$probe = New-Object Security.Cryptography.HMACSHA256(, $macKey)
$check = $probe.ComputeHash([Text.Encoding]::UTF8.GetBytes('crowns-of-kemet'))
$probe.Dispose()

$manifest = [ordered]@{
    version = 1
    cipher  = 'AES-256-CBC, then HMAC-SHA-256 over iv||ciphertext'
    kdf     = [ordered]@{
        name       = 'PBKDF2'
        hash       = 'SHA-256'
        iterations = $Iterations
        salt       = [Convert]::ToBase64String($salt)
    }
    check = [Convert]::ToBase64String($check)
    files = $files
}

$json = $manifest | ConvertTo-Json -Depth 6
[IO.File]::WriteAllText(
    (Join-Path $outDir 'manifest.json'), $json, (New-Object Text.UTF8Encoding($false)))

Copy-Item (Join-Path $sourceDir 'WebGL.loader.js') (Join-Path $outDir 'WebGL.loader.js') -Force

''
"Wrote $outDir. The password is not stored anywhere; losing it means re-running this."
