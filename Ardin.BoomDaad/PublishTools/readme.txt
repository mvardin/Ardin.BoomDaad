dotnet publish -f net8.0-android -c Release /p:AndroidPackageFormat=aab

java -jar bundlesigner-0.1.13.jar genbin  -v --bundle in.ardin.BoomDaad.aab --bin .  --v2-signing-enabled true --v3-signing-enabled false --ks playkon.keystore 


166328600