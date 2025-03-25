docker ps -q | ForEach-Object { docker stop $_ }; docker ps -a -q | ForEach-Object { docker rm $_ }; docker images -q | ForEach-Object { docker rmi $_ }; docker system prune -a --volumes -f

# Define variables for certificate generation
$sslFolderName = "ssl"
$privateKeyFile = "avancira.key"
$certificateFile = "avancira.crt"
$pfxFile = "avancira.pfx"
$passphrase = "123456"
$daysValid = 365
$country = "AU"
$state = "NSW"
$locality = "Strathfield"
$organization = ""
$organizationalUnit = "IT"
$commonName = ""
$emailAddress = ""

# Function to generate SSL certificates and update appsettings
function Generate-SSLCertificates {
    param (
        [string]$dockerName,
        [string]$folderPath,
        [string]$appSettingsFile
    )

    Write-Host "Navigating to $folderPath folder..." -ForegroundColor Cyan
    Set-Location $folderPath

    # Delete 'ssl' folder if it exists
    if (Test-Path -Path .\$sslFolderName) {
        Write-Host "Deleting existing '$sslFolderName' folder in $folderPath..." -ForegroundColor Red
        Remove-Item -Recurse -Force .\$sslFolderName
    }
    
    # Check if 'ssl' folder exists, and create it if it doesn't
    if (-Not (Test-Path -Path .\$sslFolderName)) {
        Write-Host "Creating 'ssl' folder in $folderPath..." -ForegroundColor Cyan
        mkdir $sslFolderName

        # openssl req -x509 -newkey rsa:4096 -keyout ssl/avancira.key -out ssl/avancira.crt -days 365
        # openssl pkcs12 -export -out ssl/certificate.pfx -inkey ssl/avancira.key -in ssl/avancira.crt
        # openssl x509 -in ssl/avancira.crt -noout -text


        # Generate SSL files
        Write-Host "Generating certificates in $folderPath..." -ForegroundColor Cyan
        openssl req -x509 -newkey rsa:4096 -keyout .\$sslFolderName\$privateKeyFile -out .\$sslFolderName\$certificateFile -days $daysValid -subj "/C=$country/ST=$state/L=$locality/O=$organization/OU=$organizationalUnit/CN=$commonName/emailAddress=$emailAddress"
        openssl pkcs12 -export -out .\$sslFolderName\$pfxFile -inkey .\$sslFolderName\$privateKeyFile -in .\$sslFolderName\$certificateFile -passout pass:$passphrase
        openssl x509 -in .\$sslFolderName\$certificateFile -noout -text
    } else {
        Write-Host "'$sslFolderName' folder already exists in $folderPath. Skipping creation and SSL generation." -ForegroundColor Yellow
    }

    # Update the appsettings.Production.json with the new certificate details
    Write-Host "Updating $appSettingsFile with the generated certificate details..." -ForegroundColor Cyan

    # Read the current JSON file
    $appSettings = Get-Content -Path $appSettingsFile | ConvertFrom-Json

    # # Print the content of $appSettings
    # Write-Host "Current appsettings content:" -ForegroundColor Cyan
    # Write-Host ($appSettings | ConvertTo-Json -Depth 10)

    # Update the certificate paths and password
    $appSettings.Kestrel.Endpoints.Https.Certificate.Path = "/$dockerName/$sslFolderName/$certificateFile"
    $appSettings.Kestrel.Endpoints.Https.Certificate.KeyPath = "/$dockerName/$sslFolderName/$privateKeyFile"
    $appSettings.Kestrel.Endpoints.Https.Certificate.Password = $passphrase

    # Change 4 spaces to 2 for indentation
    # $appSettings = $appSettings -replace "    ", "  "
    # Save the updated JSON file
    $appSettings | ConvertTo-Json -Depth 10 | Set-Content -Path $appSettingsFile

    # Navigate back to root
    Write-Host "Returning to root folder..." -ForegroundColor Cyan
    Set-Location ..
}

# Generate certificates and update Backend appsettings
# Generate-SSLCertificates -dockerName "backend" -folderPath ".\Backend" -appSettingsFile ".\appsettings.Production.json"

# Generate certificates and update Frontend appsettings
# Generate-SSLCertificates  -dockerName "frontend" -folderPath ".\Frontend" -appSettingsFile ".\appsettings.Production.json"

# Run Docker Compose
Write-Host "Running Docker Compose..." -ForegroundColor Cyan
docker compose up -d

Write-Host "Task completed!" -ForegroundColor Green
