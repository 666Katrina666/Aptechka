[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Write-Host @"
Проверка GitHub-токена для Aptechka.
Токен НЕ будет показан и НЕ сохранится в файл.
Вставь токен и нажми Enter (символы могут не отображаться).
"@

$secure = Read-Host -AsSecureString 'GitHub token'
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try {
    $token = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
}

$token = $token.Trim()
if ($token.Length -lt 20) {
    throw 'Токен слишком короткий. Скопируй его целиком.'
}

function Invoke-GitHubCheck {
    param(
        [string]$Label,
        [string]$Uri
    )

    $headers = @{
        Authorization = "Bearer $token"
        Accept = 'application/vnd.github+json'
        'X-GitHub-Api-Version' = '2022-11-28'
        'User-Agent' = 'Aptechka-token-check/0.1'
    }

    try {
        $response = Invoke-WebRequest -Uri $Uri -Headers $headers -Method Get -UseBasicParsing
        Write-Host ("[{0}] {1} OK" -f $response.StatusCode, $Label)
        return $response.Content
    }
    catch {
        $status = $null
        $body = $null
        if ($_.Exception.Response) {
            $status = [int]$_.Exception.Response.StatusCode
            try {
                $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
                $body = $reader.ReadToEnd()
                $reader.Close()
            }
            catch {
                $body = $_.ErrorDetails.Message
            }
        }
        else {
            $body = $_.Exception.Message
        }

        $message = $body
        try {
            $json = $body | ConvertFrom-Json
            if ($json.message) { $message = $json.message }
        }
        catch { }

        Write-Host ("[{0}] {1} FAIL: {2}" -f $(if ($status) { $status } else { 'ERR' }), $Label, $message)
        return $null
    }
}

Write-Host ''
Write-Host '=== Кто этот токен ==='
$userJson = Invoke-GitHubCheck -Label 'GET /user' -Uri 'https://api.github.com/user'
if ($userJson) {
    $user = $userJson | ConvertFrom-Json
    Write-Host ("login={0}" -f $user.login)
}

Write-Host ''
Write-Host '=== Доступ к aptechka-data ==='
$repoJson = Invoke-GitHubCheck -Label 'GET /repos/666Katrina666/aptechka-data' -Uri 'https://api.github.com/repos/666Katrina666/aptechka-data'
if ($repoJson) {
    $repo = $repoJson | ConvertFrom-Json
    Write-Host ("full_name={0} private={1} default_branch={2}" -f $repo.full_name, $repo.private, $repo.default_branch)
    if ($repo.permissions) {
        Write-Host ("permissions admin={0} push={1} pull={2}" -f $repo.permissions.admin, $repo.permissions.push, $repo.permissions.pull)
    }
}

Write-Host ''
Write-Host '=== Ветка main ==='
Invoke-GitHubCheck -Label 'GET git/ref/heads/main' -Uri 'https://api.github.com/repos/666Katrina666/aptechka-data/git/ref/heads/main' | Out-Null

Write-Host ''
Write-Host '=== Тот же запрос с Api-Version как в приложении (2026-03-10) ==='
$headersOld = @{
    Authorization = "Bearer $token"
    Accept = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2026-03-10'
    'User-Agent' = 'Aptechka-token-check/0.1'
}
try {
    $r = Invoke-WebRequest -Uri 'https://api.github.com/repos/666Katrina666/aptechka-data' -Headers $headersOld -Method Get -UseBasicParsing
    Write-Host ("[{0}] app Api-Version OK" -f $r.StatusCode)
}
catch {
    $status = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 'ERR' }
    Write-Host ("[{0}] app Api-Version FAIL" -f $status)
}

Write-Host ''
Write-Host 'Если GET /user = OK, а aptechka-data = 404 — у токена нет доступа к этому приватному репозиторию.'
Write-Host 'Если оба OK — токен хороший; тогда проблема в полях/сборке приложения.'

$token = $null
[GC]::Collect()
