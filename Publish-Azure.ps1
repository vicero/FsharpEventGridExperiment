$webServerA="WebServerA"
$webServerB="WebServerB"

$webServerA, $webServerB | ForEach-Object {
    # Get publishing profile for the web app
    $xml = [xml](Get-AzWebAppPublishingProfile -Name $_ `
    -ResourceGroupName JAMESDEV `
    -OutputFile null)

    # Extract connection information from publishing profile
    $username = $xml.SelectNodes("//publishProfile[@publishMethod=`"FTP`"]/@userName").value
    $password = $xml.SelectNodes("//publishProfile[@publishMethod=`"FTP`"]/@userPWD").value
    $url = $xml.SelectNodes("//publishProfile[@publishMethod=`"FTP`"]/@publishUrl").value

    # Upload files recursively 
    Set-Location $PSScriptRoot
    $webclient = New-Object -TypeName System.Net.WebClient
    $webclient.Credentials = New-Object System.Net.NetworkCredential($username,$password)
    $excludes = "_ReSharper*", ".idea*", "*.ps1"
    $files = Get-ChildItem -Exclude $excludes | Get-ChildItem -Recurse
    foreach ($file in $files)
    {
        $relativepath = (Resolve-Path -Path $file.FullName -Relative).Replace(".\", "").Replace('\', '/')
        $uri = New-Object System.Uri("$url/$relativepath")
        "Uploading to " + $uri.AbsoluteUri
        $webclient.UploadFile($uri, $file.FullName)
    }
    $webclient.Dispose()
}