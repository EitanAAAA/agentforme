param(
    [Parameter(Mandatory = $true)]
    [string]$TargetPath
)

$ErrorActionPreference = 'Stop'

$OutputDirectory = Split-Path -Parent $TargetPath

if (-not (Test-Path -LiteralPath $OutputDirectory)) {
    exit 0
}

$subject = 'CN=SMT Agent Local Dev'
$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
    Where-Object Subject -eq $subject |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if (-not $cert) {
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $subject `
        -CertStoreLocation Cert:\CurrentUser\My `
        -KeyUsage DigitalSignature `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -HashAlgorithm SHA256 `
        -NotAfter (Get-Date).AddYears(3)
}

foreach ($storeName in @('Root', 'TrustedPublisher')) {
    $storePath = "Cert:\CurrentUser\$storeName\$($cert.Thumbprint)"
    if (-not (Test-Path $storePath)) {
        $store = [System.Security.Cryptography.X509Certificates.X509Store]::new($storeName, 'CurrentUser')
        $store.Open('ReadWrite')
        $store.Add($cert)
        $store.Close()
    }
}

Get-ChildItem -LiteralPath $OutputDirectory -File |
    Where-Object Extension -in '.exe', '.dll' |
    ForEach-Object {
        Set-AuthenticodeSignature -FilePath $_.FullName -Certificate $cert -HashAlgorithm SHA256 | Out-Null
    }
