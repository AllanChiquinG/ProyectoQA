# Guarda el token de ngrok en .env.ngrok (ignorado por Git). El token no se muestra al pegarlo ni queda en el historial.
$secure = Read-Host 'Pega tu authtoken de ngrok y presiona Enter' -AsSecureString
$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try {
    $token = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer).Trim()
} finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
}
if ($token.Length -lt 20) { Write-Host 'El token parece incompleto. No se guardó nada.'; exit 1 }
$path = Join-Path (Split-Path $PSScriptRoot -Parent) '.env.ngrok'
[IO.File]::WriteAllText($path, "NGROK_AUTHTOKEN=$token`n", (New-Object Text.UTF8Encoding($false)))
Write-Host "Guardado en $path"
