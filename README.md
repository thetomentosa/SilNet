<p align="center"><img src="Assets/SilNet.png" width="96" alt="SilNet kalpli S logosu"></p>
<h1 align="center">SilNet</h1>
<p align="center">Ücretsiz, hesapsız Windows bağlantı yardımcısı.</p>
<p align="center"><a href="https://github.com/thetomentosa/SilNet/releases/latest">Windows için indir</a> · <a href="https://github.com/thetomentosa/SilNet/issues">Sorun bildir</a></p>

SilNet, Zapret2 motorunu basit bir Türkçe arayüzle yönetir. Sağlayıcını seçip **Bağlan** düğmesine basarsın. Uygulama önce mevcut erişimi kontrol eder, gerekirse sağlayıcına göre sıralanmış yöntemleri dener. Doğrulanan son yöntem o ağ için yalnızca cihazında hatırlanır.

> Bütün internet sağlayıcılarında çalışma veya engel aşımı garantisi yoktur. VPN değildir; IP adresini gizlemez.

## İndir ve kullan

1. [Son sürümden](https://github.com/thetomentosa/SilNet/releases/latest) **SilNet-Setup-…-win-x64.exe** dosyasını indirip kur. Kurulum mevcut kullanıcı içindir.
2. SilNet’i aç, internet sağlayıcını seç. Bilmiyorsan **Otomatik / bilmiyorum** seçili kalabilir.
3. İlk kurulum adımlarında sağlayıcını ve 14 uygulama arasından hedeflerini seç. Özel alan adları Uygulamalar bölümünden eklenebilir.
4. **Bağlan** düğmesine bas. Windows’un motor için gösterdiği yönetici iznini onayla.
5. Sonucu ana ekranda, ayrıntıları **Tanılama** bölümünde gör.
6. **Durdur** motoru durdurur. X düğmesi uygulamayı sistem tepsisine küçültür; tamamen kapatmak için tepsi menüsünden **Çıkış** seçilir.

Taşınabilir ZIP de sunulur. Tamamını aynı klasöre çıkarıp `SilNet.exe` dosyasını aç. EXE’yi tek başına başka yere taşıma: `engines` klasörü de gereklidir. .NET çalışma zamanı pakete dahildir; kullanıcıdan ek yazılım, Inno Setup veya ücretli hizmet kurulumu istenmez.

Windows 10 2004 veya sonrası / Windows 11, **x64** gerektirir. ARM64 desteklenmez. İmzasız bir önizlemedir; Windows yayıncı/SmartScreen uyarısı gösterebilir. Güvenlik korumalarını otomatik kapatmaz.

## Sağlayıcılar ve bağlantı hafızası

Türk Telekom, Superonline, TurkNet, Vodafone Net, Kablonet, Millenicom, Netspeed, Comnet ve üç büyük mobil sağlayıcı için deneme sıraları bulunur. Bunlar sağlayıcıların onayladığı veya bütün hatlarda doğrulanmış profiller değildir. Aynı sağlayıcıda bile bölge, modem, altyapı ve rota sonucu değiştirebilir.

- Türkiye önayarları SplitWire-Turkey’in MIT lisanslı `presets2.txt` dosyasındaki yöntemlerden uyarlanmıştır.
- TurkNet ve alternatif sağlayıcılardaki sıralar genel yöntem önerileridir; o ağda test edilerek doğrulanır.
- Ağ kimliği adaptör ve ağ geçidinden türetilip yerel olarak hash’lenir. Bu bilgi bir sunucuya gönderilmez.
- Son başarılı yöntem önce denenir; ağ değişirse önceki test yeni ağa uygulanmaz.
- “Uygun yöntemi otomatik bul” kapatılarak elle yöntem seçilebilir.

Otomatik seçim tam blockcheck değildir: seçilen hedeflerin en fazla üçü, sınırlı sayıda aday yöntemle HTTPS üzerinden sınanır. HTTP 403/429, DNS arızası ve hedef sunucu hataları da erişimin doğrulanamamasına neden olabilir. Kısmi erişim açıkça belirtilir; tamamen başarılı profil olarak kaydedilmez. Seçili alan adlarının UDP DNS sorguları isteğe bağlı geçici DoH desteğiyle çözümlenir. Google ve Cloudflare çözümleyicileri kullanılır; kalıcı adaptör DNS ayarı değiştirilmez.

## Güvenlik ve gizlilik

- Normal arayüz ve ayrı yönetici yardımcısı; kullanıcı SID’sine özel pipe ACL’i ve her iki yönde süreç kimliği doğrulaması.
- UAC sonrası farklı yetki düzeylerinin iletişimini kesen eski `CurrentUserOnly` yaklaşımı kaldırıldı.
- İlk komuttan önce protokol el sıkışması; komutlara özel yanıt kimliği; hata ve yeniden bağlanma yönetimi.
- Motor ve Lua dosyalarında uygulamaya gömülü SHA-256 doğrulaması.
- SilNet’in motoru Windows Job Object ile yönetilir; yardımcı kapanınca kendi motoru durur.
- Başka DPI yazılımlarını, ortak sürücü hizmetlerini veya antivirüs programlarını silmez.
- DNS, güvenlik duvarı, kalıcı servis ve otomatik başlangıç ayarlarını değiştirmez. Motor açıldığında Windows WinDivert sürücüsünü yükleyebilir.
- Telemetri veya hesap sistemi yoktur. Açılışta etkinse GitHub sürüm bilgisi istenir; bağlantı testleri seçili sitelerle doğrudan iletişim kurar.

Ayarlar `%LOCALAPPDATA%\SilNet\settings.json`, günlükler `%LOCALAPPDATA%\SilNet\logs` altındadır. Günlüklerde alan adları bulunabilir; paylaşmadan önce incele. Motor ve arayüz aynı Windows kullanıcı hesabıyla çalışmalıdır. Farklı bir yönetici hesabının kimlik bilgileriyle UAC kullanımı desteklenmez.

## Güncellemeler

SilNet açılışta ve elle kontrol istendiğinde yalnızca **thetomentosa/SilNet** deposunun son kararlı GitHub Release bilgisini kontrol eder. Yeni sürüm varsa üstteki düğmede gösterir. Düğme resmî yayın sayfasını açar; yeni kurulum paketini indirip çalıştırarak aynı kurulumun üzerine güncelleyebilirsin. Yerel ayarlar korunur. Habersiz indirme, dosya değiştirme veya otomatik EXE çalıştırma yapmaz.

Otomatik kontrol **Ayarlar** bölümünden kapatılabilir. GitHub erişilemiyorsa bağlantı motorunun çalışmasını engellemez. Yeni sürümler için proje `Version` değeri ve `vX.Y.Z` Git etiketi aynı olmalıdır; release iş akışı bunu denetler.

## Kapsam

Varsayılan motor kapsamı HTTPS (TCP 443) ile sınırlıdır. QUIC (UDP 443) desteği Ayarlar bölümünden isteğe bağlı açılır ve varsayılan kapalıdır. HTTPS test süresi DNS, bağlantı ve HTTP yanıtını kapsar; Discord ses gecikmesi veya oyun pingi değildir. Sıfır gecikme etkisi garanti edilmez. Discord sesli görüşmesi/STUN, konsollara ağ paylaşımı ve uygulama bazlı VPN tünellemesi bu sürümde yoktur. Siteye HTTPS erişimi; hesap girişini, bütün uygulama işlevlerini veya sesli görüşmeyi kanıtlamaz.

## Kaynaktan derle

Gereksinimler: Windows x64, .NET SDK **10**, Python 3. Kurulum EXE’si için ücretsiz/açık kaynak proje kullanımına uygun Inno Setup 6.3+ veya 7 gerekir. Geliştirme araçları son kullanıcı için gerekli değildir.

```powershell
python prepare_engine.py
dotnet build SilNet.csproj -c Release
./scripts/build.ps1 -Iscc 'C:\Program Files\Inno Setup 7\ISCC.exe'
# Yalnızca taşınabilir paket:
./scripts/build.ps1 -SkipInstaller
```

Motor EXE/DLL dosyaları Git deposuna eklenmez; sabit upstream snapshot’tan indirilir ve `engine-integrity.json` ile doğrulanır. WinDivert, geliştiricisinin resmî 2.2.2-A paketinden alınır. `licenses/sources` içindeki kaynak arşivlerini dağıtımlarda koru.

```powershell
./artifacts/portable/SilNet.exe --self-test "$PWD/test-results.json"
# Windows yönetici izni ister, gerçek motoru açıp kapatır:
./artifacts/portable/SilNet.exe --elevated-test "$PWD/elevated-results.json"
./artifacts/portable/SilNet.exe --preview "$PWD/preview.png"
```

Self-test alan adı doğrulamasını, değiştirilmiş motor dosyasını reddetmeyi, gerçek Windows alt süreç temizliğini, ayrı yardımcı süreçle pipe bağlantısını, yeniden bağlantıyı ve sürüm adresi doğrulamasını sınar. Elevated-test profilleri gerçek motorun `--dry-run` seçeneğiyle sınar, motoru başlatıp durdurur ve Discord/Roblox HTTPS kontrollerini raporlar; DNS ayarını değiştirmez.

## GitHub yayını

`main` değişiklikleri Windows CI’da derlenir ve test edilir. `vX.Y.Z` etiketi gönderilince workflow kurulum EXE’si, taşınabilir ZIP ve SHA-256 listesiyle GitHub Release oluşturur. Sürüm notları `RELEASE_NOTES.md` dosyasından alınır.

```powershell
# SilNet.csproj Version ve RELEASE_NOTES.md güncellendikten sonra:
git add .
git commit -m "Release X.Y.Z"
git tag vX.Y.Z
git push origin main --tags
```

MIT lisansı yalnızca özgün SilNet koduna uygulanır. Üçüncü taraf bileşenlerin lisansları ve kaynakları `licenses` klasöründedir. Kalpli S logosu ImageGen ile üretilmiştir; son tasarım mavi gradyandır.

## Kaldırma

Windows Yüklü uygulamalar veya SilNet Ayarlar bölümündeki kaldırıcı kullanılabilir. Kaldırıcı kişisel ayar ve günlükleri temizlemeyi ayrıca sorar; sessiz kaldırma bunları korur. Taşınabilir sürümde önce tepsi menüsünden çıkıp klasörü kaldır. SilNet başka programların ortak sürücülerini kaldırmaz.
