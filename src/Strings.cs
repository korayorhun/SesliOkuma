using System;
using System.Collections.Generic;
using System.Globalization;

namespace SesliOkuma
{
    // UI localization. Keys are looked up in the active language, then English, then returned raw.
    public static class L
    {
        public static readonly string[] Languages = { "tr", "en", "zh", "hi", "es", "fr", "ar", "pt" };
        public static string Lang = "tr";

        public static bool IsRtl { get { return Lang == "ar"; } }

        public static string NativeName(string code)
        {
            switch (code)
            {
                case "tr": return "Türkçe";
                case "en": return "English";
                case "zh": return "中文";
                case "hi": return "हिन्दी";
                case "es": return "Español";
                case "fr": return "Français";
                case "ar": return "العربية";
                case "pt": return "Português";
            }
            return code;
        }

        public static string DetectSystemLanguage()
        {
            string two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            foreach (string l in Languages) if (l == two) return l;
            return "en";
        }

        public static string T(string key)
        {
            Dictionary<string, string> d;
            string s;
            if (D.TryGetValue(Lang, out d) && d.TryGetValue(key, out s)) return s;
            if (D.TryGetValue("en", out d) && d.TryGetValue(key, out s)) return s;
            return key;
        }

        public static string F(string key, params object[] args) { return string.Format(T(key), args); }

        // Sample sentence spoken when previewing a voice, keyed by the voice's language.
        public static string Sample(string lang2, string voiceName)
        {
            switch (lang2)
            {
                case "tr": return "Merhaba, ben " + voiceName + ". Seçtiğiniz metinleri bu sesle okuyacağım.";
                case "zh": return "你好，我是" + voiceName + "。我将用这个声音朗读您选择的文字。";
                case "hi": return "नमस्ते, मैं " + voiceName + " हूँ। मैं आपके चुने हुए पाठ को इस आवाज़ में पढ़ूँगा।";
                case "es": return "Hola, soy " + voiceName + ". Leeré el texto que selecciones con esta voz.";
                case "fr": return "Bonjour, je suis " + voiceName + ". Je lirai le texte sélectionné avec cette voix.";
                case "ar": return "مرحباً، أنا " + voiceName + ". سأقرأ النص الذي تحدده بهذا الصوت.";
                case "pt": return "Olá, eu sou " + voiceName + ". Vou ler o texto selecionado com esta voz.";
                case "de": return "Hallo, ich bin " + voiceName + ". Ich lese den markierten Text mit dieser Stimme.";
                case "ru": return "Здравствуйте, я " + voiceName + ". Я прочитаю выделенный текст этим голосом.";
                case "ja": return "こんにちは、" + voiceName + "です。選択したテキストをこの声で読み上げます。";
                case "ko": return "안녕하세요, 저는 " + voiceName + "입니다. 선택한 텍스트를 이 목소리로 읽어 드리겠습니다.";
                case "it": return "Ciao, sono " + voiceName + ". Leggerò il testo selezionato con questa voce.";
            }
            return "Hi, I'm " + voiceName + ". I will read your selected text with this voice.";
        }

        static readonly Dictionary<string, Dictionary<string, string>> D = Build();

        static Dictionary<string, Dictionary<string, string>> Build()
        {
            var all = new Dictionary<string, Dictionary<string, string>>();

            all["tr"] = new Dictionary<string, string> {
                {"Subtitle", "{0}  seçili metni okur  ·  tekrar basınca susar"},
                {"Hotkey", "KISAYOL"},
                {"HotkeyHint", "Tıkla: değiştir  ·  2×: pano  ·  basılı tut: duraklat"},
                {"HotkeyCapture", "Yeni tuş kombinasyonuna basın…  (Esc iptal)"},
                {"HotkeyNeedMod", "Ctrl, Alt, Shift veya Win ile birlikte bir tuş seçin"},
                {"HotkeyTaken", "{0} başka bir uygulama tarafından kullanılıyor"},
                {"HotkeySaved", "Kısayol: {0}"},
                {"Primary", "ANA DİL"},
                {"Other", "DİĞER DİLLER"},
                {"NoVoice", "Ses seçilmedi"},
                {"PickVoice", "Tıklayıp bir ses seçin"},
                {"Natural", "doğal ses"},
                {"Classic", "klasik ses"},
                {"Multilingual", "çok dilli"},
                {"Speed", "OKUMA HIZI"},
                {"Normal", "Normal"},
                {"Fast", "Hızlı"},
                {"Slow", "Yavaş"},
                {"StartWithWindows", "Windows ile başlat"},
                {"AutoUpdate", "Güncellemeleri otomatik denetle"},
                {"Language", "Arayüz dili"},
                {"Check", "denetle"},
                {"Checking", "Güncelleme denetleniyor…"},
                {"UpToDate", "Güncel: {0}"},
                {"CheckFailed", "Güncelleme denetlenemedi (ağ?)"},
                {"NewVersion", "Yeni sürüm {0} hazır"},
                {"ReleaseNotes", "Sürüm notları için tıklayın"},
                {"Update", "Güncelle"},
                {"Skipped", "Bu sürüm atlandı"},
                {"Downloading", "İndiriliyor…  %{0}"},
                {"Installing", "Doğrulanıyor ve kuruluyor…"},
                {"RestartSoon", "Uygulama birkaç saniye içinde yeniden başlayacak"},
                {"UpdateClick", "Güncellemek için tıklayın."},
                {"NaturalTitle", "Doğal, insan gibi sesler"},
                {"NaturalText", "Emel ve Ahmet dahil neural sesleri ücretsiz ekleyin"},
                {"Install", "Kur"},
                {"NaturalInstalling", "Doğal sesler kuruluyor…  %{0}"},
                {"NaturalRegistering", "Sesler sisteme kaydediliyor (yönetici onayı)…"},
                {"NaturalDone", "Doğal sesler eklendi"},
                {"NaturalFailed", "Kurulum tamamlanamadı: {0}"},
                {"NaturalNote", "Açık kaynak NaturalVoiceSAPIAdapter · Microsoft çevrimiçi sesleri · internet gerekir"},
                {"MoreVoices", "Windows ses paketleri ekle…"},
                {"Settings", "Ayarlar"},
                {"Stop", "Sustur"},
                {"CheckUpdates", "Güncellemeleri denetle"},
                {"Exit", "Çıkış"},
                {"VoiceSelected", "{0} seçildi"},
                {"StartupAdded", "Başlangıca eklendi"},
                {"StartupRemoved", "Başlangıçtan kaldırıldı"},
                {"HotkeyFailBalloon", "{0} kaydedilemedi (başka uygulama kullanıyor)"},
                {"TrayTip", "Sesli Okuma  ·  {0}"},
                {"Ready", "Sesli Okuma {0} hazır"},
                {"Cancelled", "İptal edildi"},
            };

            all["en"] = new Dictionary<string, string> {
                {"Subtitle", "{0}  reads the selected text  ·  press again to stop"},
                {"Hotkey", "SHORTCUT"},
                {"HotkeyHint", "Click: change  ·  2×: clipboard  ·  hold: pause"},
                {"HotkeyCapture", "Press the new key combination…  (Esc to cancel)"},
                {"HotkeyNeedMod", "Combine a key with Ctrl, Alt, Shift or Win"},
                {"HotkeyTaken", "{0} is already used by another application"},
                {"HotkeySaved", "Shortcut: {0}"},
                {"Primary", "PRIMARY LANGUAGE"},
                {"Other", "OTHER LANGUAGES"},
                {"NoVoice", "No voice selected"},
                {"PickVoice", "Click to choose a voice"},
                {"Natural", "natural voice"},
                {"Classic", "classic voice"},
                {"Multilingual", "multilingual"},
                {"Speed", "READING SPEED"},
                {"Normal", "Normal"},
                {"Fast", "Fast"},
                {"Slow", "Slow"},
                {"StartWithWindows", "Start with Windows"},
                {"AutoUpdate", "Check for updates automatically"},
                {"Language", "Interface language"},
                {"Check", "check"},
                {"Checking", "Checking for updates…"},
                {"UpToDate", "Up to date: {0}"},
                {"CheckFailed", "Update check failed (network?)"},
                {"NewVersion", "Version {0} is available"},
                {"ReleaseNotes", "Click for release notes"},
                {"Update", "Update"},
                {"Skipped", "This version was skipped"},
                {"Downloading", "Downloading…  {0}%"},
                {"Installing", "Verifying and installing…"},
                {"RestartSoon", "The app will restart in a few seconds"},
                {"UpdateClick", "Click to update."},
                {"NaturalTitle", "Natural, human-like voices"},
                {"NaturalText", "Add free neural voices for many languages"},
                {"Install", "Install"},
                {"NaturalInstalling", "Installing natural voices…  {0}%"},
                {"NaturalRegistering", "Registering voices (administrator approval)…"},
                {"NaturalDone", "Natural voices added"},
                {"NaturalFailed", "Installation did not complete: {0}"},
                {"NaturalNote", "Open-source NaturalVoiceSAPIAdapter · Microsoft online voices · requires internet"},
                {"MoreVoices", "Add Windows voice packs…"},
                {"Settings", "Settings"},
                {"Stop", "Stop"},
                {"CheckUpdates", "Check for updates"},
                {"Exit", "Exit"},
                {"VoiceSelected", "{0} selected"},
                {"StartupAdded", "Added to startup"},
                {"StartupRemoved", "Removed from startup"},
                {"HotkeyFailBalloon", "Could not register {0} (used by another app)"},
                {"TrayTip", "Sesli Okuma  ·  {0}"},
                {"Ready", "Sesli Okuma {0} is ready"},
                {"Cancelled", "Cancelled"},
            };

            all["zh"] = new Dictionary<string, string> {
                {"Subtitle", "{0}  朗读选中的文字  ·  再按一次停止"},
                {"Hotkey", "快捷键"},
                {"HotkeyHint", "点击：更改  ·  按两次：剪贴板  ·  长按：暂停"},
                {"HotkeyCapture", "请按下新的组合键…  (Esc 取消)"},
                {"HotkeyNeedMod", "请与 Ctrl、Alt、Shift 或 Win 组合使用"},
                {"HotkeyTaken", "{0} 已被其他应用占用"},
                {"HotkeySaved", "快捷键：{0}"},
                {"Primary", "主要语言"},
                {"Other", "其他语言"},
                {"NoVoice", "未选择语音"},
                {"PickVoice", "点击选择语音"},
                {"Natural", "自然语音"},
                {"Classic", "经典语音"},
                {"Multilingual", "多语言"},
                {"Speed", "朗读速度"},
                {"Normal", "正常"},
                {"Fast", "快"},
                {"Slow", "慢"},
                {"StartWithWindows", "随 Windows 启动"},
                {"AutoUpdate", "自动检查更新"},
                {"Language", "界面语言"},
                {"Check", "检查"},
                {"Checking", "正在检查更新…"},
                {"UpToDate", "已是最新：{0}"},
                {"CheckFailed", "无法检查更新（网络？）"},
                {"NewVersion", "新版本 {0} 可用"},
                {"ReleaseNotes", "点击查看更新说明"},
                {"Update", "更新"},
                {"Skipped", "已跳过此版本"},
                {"Downloading", "正在下载…  {0}%"},
                {"Installing", "正在验证并安装…"},
                {"RestartSoon", "应用将在几秒后重新启动"},
                {"UpdateClick", "点击更新。"},
                {"NaturalTitle", "自然、拟人的语音"},
                {"NaturalText", "免费添加多种语言的神经网络语音"},
                {"Install", "安装"},
                {"NaturalInstalling", "正在安装自然语音…  {0}%"},
                {"NaturalRegistering", "正在注册语音（需要管理员确认）…"},
                {"NaturalDone", "已添加自然语音"},
                {"NaturalFailed", "安装未完成：{0}"},
                {"NaturalNote", "开源 NaturalVoiceSAPIAdapter · Microsoft 在线语音 · 需要网络"},
                {"MoreVoices", "添加 Windows 语音包…"},
                {"Settings", "设置"},
                {"Stop", "停止"},
                {"CheckUpdates", "检查更新"},
                {"Exit", "退出"},
                {"VoiceSelected", "已选择 {0}"},
                {"StartupAdded", "已添加到启动项"},
                {"StartupRemoved", "已从启动项移除"},
                {"HotkeyFailBalloon", "无法注册 {0}（被其他应用占用）"},
                {"TrayTip", "Sesli Okuma  ·  {0}"},
                {"Ready", "Sesli Okuma {0} 已就绪"},
                {"Cancelled", "已取消"},
            };

            all["hi"] = new Dictionary<string, string> {
                {"Subtitle", "{0}  चयनित पाठ पढ़ता है  ·  रोकने के लिए फिर दबाएँ"},
                {"Hotkey", "शॉर्टकट"},
                {"HotkeyHint", "क्लिक: बदलें  ·  2×: क्लिपबोर्ड  ·  दबाए रखें: रोकें"},
                {"HotkeyCapture", "नई कुंजी संयोजन दबाएँ…  (रद्द करने के लिए Esc)"},
                {"HotkeyNeedMod", "किसी कुंजी को Ctrl, Alt, Shift या Win के साथ मिलाएँ"},
                {"HotkeyTaken", "{0} पहले से किसी अन्य ऐप द्वारा उपयोग में है"},
                {"HotkeySaved", "शॉर्टकट: {0}"},
                {"Primary", "मुख्य भाषा"},
                {"Other", "अन्य भाषाएँ"},
                {"NoVoice", "कोई आवाज़ चयनित नहीं"},
                {"PickVoice", "आवाज़ चुनने के लिए क्लिक करें"},
                {"Natural", "प्राकृतिक आवाज़"},
                {"Classic", "क्लासिक आवाज़"},
                {"Multilingual", "बहुभाषी"},
                {"Speed", "पढ़ने की गति"},
                {"Normal", "सामान्य"},
                {"Fast", "तेज़"},
                {"Slow", "धीमा"},
                {"StartWithWindows", "Windows के साथ शुरू करें"},
                {"AutoUpdate", "अपडेट स्वतः जाँचें"},
                {"Language", "इंटरफ़ेस भाषा"},
                {"Check", "जाँचें"},
                {"Checking", "अपडेट जाँचे जा रहे हैं…"},
                {"UpToDate", "नवीनतम: {0}"},
                {"CheckFailed", "अपडेट जाँच विफल (नेटवर्क?)"},
                {"NewVersion", "संस्करण {0} उपलब्ध है"},
                {"ReleaseNotes", "रिलीज़ नोट्स के लिए क्लिक करें"},
                {"Update", "अपडेट"},
                {"Skipped", "यह संस्करण छोड़ दिया गया"},
                {"Downloading", "डाउनलोड हो रहा है…  {0}%"},
                {"Installing", "सत्यापन और इंस्टॉल हो रहा है…"},
                {"RestartSoon", "ऐप कुछ सेकंड में पुनः प्रारंभ होगा"},
                {"UpdateClick", "अपडेट करने के लिए क्लिक करें।"},
                {"NaturalTitle", "प्राकृतिक, मानव-जैसी आवाज़ें"},
                {"NaturalText", "कई भाषाओं के लिए मुफ़्त न्यूरल आवाज़ें जोड़ें"},
                {"Install", "इंस्टॉल"},
                {"NaturalInstalling", "प्राकृतिक आवाज़ें इंस्टॉल हो रही हैं…  {0}%"},
                {"NaturalRegistering", "आवाज़ें पंजीकृत हो रही हैं (प्रशासक अनुमति)…"},
                {"NaturalDone", "प्राकृतिक आवाज़ें जोड़ी गईं"},
                {"NaturalFailed", "इंस्टॉल पूरा नहीं हुआ: {0}"},
                {"NaturalNote", "ओपन-सोर्स NaturalVoiceSAPIAdapter · Microsoft ऑनलाइन आवाज़ें · इंटरनेट आवश्यक"},
                {"MoreVoices", "Windows आवाज़ पैक जोड़ें…"},
                {"Settings", "सेटिंग्स"},
                {"Stop", "रोकें"},
                {"CheckUpdates", "अपडेट जाँचें"},
                {"Exit", "बाहर निकलें"},
                {"VoiceSelected", "{0} चयनित"},
                {"StartupAdded", "स्टार्टअप में जोड़ा गया"},
                {"StartupRemoved", "स्टार्टअप से हटाया गया"},
                {"HotkeyFailBalloon", "{0} पंजीकृत नहीं हो सका (अन्य ऐप द्वारा उपयोग में)"},
                {"TrayTip", "Sesli Okuma  ·  {0}"},
                {"Ready", "Sesli Okuma {0} तैयार है"},
                {"Cancelled", "रद्द किया गया"},
            };

            all["es"] = new Dictionary<string, string> {
                {"Subtitle", "{0}  lee el texto seleccionado  ·  pulsa otra vez para detener"},
                {"Hotkey", "ATAJO"},
                {"HotkeyHint", "Clic: cambiar  ·  2×: portapapeles  ·  mantener: pausa"},
                {"HotkeyCapture", "Pulsa la nueva combinación de teclas…  (Esc cancela)"},
                {"HotkeyNeedMod", "Combina una tecla con Ctrl, Alt, Mayús o Win"},
                {"HotkeyTaken", "{0} ya lo usa otra aplicación"},
                {"HotkeySaved", "Atajo: {0}"},
                {"Primary", "IDIOMA PRINCIPAL"},
                {"Other", "OTROS IDIOMAS"},
                {"NoVoice", "Ninguna voz seleccionada"},
                {"PickVoice", "Haz clic para elegir una voz"},
                {"Natural", "voz natural"},
                {"Classic", "voz clásica"},
                {"Multilingual", "multilingüe"},
                {"Speed", "VELOCIDAD DE LECTURA"},
                {"Normal", "Normal"},
                {"Fast", "Rápido"},
                {"Slow", "Lento"},
                {"StartWithWindows", "Iniciar con Windows"},
                {"AutoUpdate", "Buscar actualizaciones automáticamente"},
                {"Language", "Idioma de la interfaz"},
                {"Check", "comprobar"},
                {"Checking", "Buscando actualizaciones…"},
                {"UpToDate", "Actualizado: {0}"},
                {"CheckFailed", "No se pudo comprobar (¿red?)"},
                {"NewVersion", "Versión {0} disponible"},
                {"ReleaseNotes", "Haz clic para ver las novedades"},
                {"Update", "Actualizar"},
                {"Skipped", "Versión omitida"},
                {"Downloading", "Descargando…  {0}%"},
                {"Installing", "Verificando e instalando…"},
                {"RestartSoon", "La aplicación se reiniciará en unos segundos"},
                {"UpdateClick", "Haz clic para actualizar."},
                {"NaturalTitle", "Voces naturales, casi humanas"},
                {"NaturalText", "Añade voces neuronales gratuitas para muchos idiomas"},
                {"Install", "Instalar"},
                {"NaturalInstalling", "Instalando voces naturales…  {0}%"},
                {"NaturalRegistering", "Registrando voces (permiso de administrador)…"},
                {"NaturalDone", "Voces naturales añadidas"},
                {"NaturalFailed", "La instalación no se completó: {0}"},
                {"NaturalNote", "NaturalVoiceSAPIAdapter de código abierto · voces en línea de Microsoft · requiere internet"},
                {"MoreVoices", "Añadir paquetes de voz de Windows…"},
                {"Settings", "Ajustes"},
                {"Stop", "Detener"},
                {"CheckUpdates", "Buscar actualizaciones"},
                {"Exit", "Salir"},
                {"VoiceSelected", "{0} seleccionada"},
                {"StartupAdded", "Añadido al inicio"},
                {"StartupRemoved", "Quitado del inicio"},
                {"HotkeyFailBalloon", "No se pudo registrar {0} (lo usa otra app)"},
                {"TrayTip", "Sesli Okuma  ·  {0}"},
                {"Ready", "Sesli Okuma {0} está listo"},
                {"Cancelled", "Cancelado"},
            };

            all["fr"] = new Dictionary<string, string> {
                {"Subtitle", "{0}  lit le texte sélectionné  ·  appuyez à nouveau pour arrêter"},
                {"Hotkey", "RACCOURCI"},
                {"HotkeyHint", "Clic : modifier  ·  2× : presse-papiers  ·  maintenir : pause"},
                {"HotkeyCapture", "Appuyez sur la nouvelle combinaison…  (Échap pour annuler)"},
                {"HotkeyNeedMod", "Combinez une touche avec Ctrl, Alt, Maj ou Win"},
                {"HotkeyTaken", "{0} est déjà utilisé par une autre application"},
                {"HotkeySaved", "Raccourci : {0}"},
                {"Primary", "LANGUE PRINCIPALE"},
                {"Other", "AUTRES LANGUES"},
                {"NoVoice", "Aucune voix sélectionnée"},
                {"PickVoice", "Cliquez pour choisir une voix"},
                {"Natural", "voix naturelle"},
                {"Classic", "voix classique"},
                {"Multilingual", "multilingue"},
                {"Speed", "VITESSE DE LECTURE"},
                {"Normal", "Normale"},
                {"Fast", "Rapide"},
                {"Slow", "Lente"},
                {"StartWithWindows", "Démarrer avec Windows"},
                {"AutoUpdate", "Rechercher les mises à jour automatiquement"},
                {"Language", "Langue de l'interface"},
                {"Check", "vérifier"},
                {"Checking", "Recherche de mises à jour…"},
                {"UpToDate", "À jour : {0}"},
                {"CheckFailed", "Vérification impossible (réseau ?)"},
                {"NewVersion", "Version {0} disponible"},
                {"ReleaseNotes", "Cliquez pour les notes de version"},
                {"Update", "Mettre à jour"},
                {"Skipped", "Version ignorée"},
                {"Downloading", "Téléchargement…  {0}%"},
                {"Installing", "Vérification et installation…"},
                {"RestartSoon", "L'application redémarre dans quelques secondes"},
                {"UpdateClick", "Cliquez pour mettre à jour."},
                {"NaturalTitle", "Voix naturelles, presque humaines"},
                {"NaturalText", "Ajoutez gratuitement des voix neuronales pour de nombreuses langues"},
                {"Install", "Installer"},
                {"NaturalInstalling", "Installation des voix naturelles…  {0}%"},
                {"NaturalRegistering", "Enregistrement des voix (autorisation administrateur)…"},
                {"NaturalDone", "Voix naturelles ajoutées"},
                {"NaturalFailed", "Installation incomplète : {0}"},
                {"NaturalNote", "NaturalVoiceSAPIAdapter open source · voix en ligne Microsoft · connexion requise"},
                {"MoreVoices", "Ajouter des packs de voix Windows…"},
                {"Settings", "Paramètres"},
                {"Stop", "Arrêter"},
                {"CheckUpdates", "Rechercher les mises à jour"},
                {"Exit", "Quitter"},
                {"VoiceSelected", "{0} sélectionnée"},
                {"StartupAdded", "Ajouté au démarrage"},
                {"StartupRemoved", "Retiré du démarrage"},
                {"HotkeyFailBalloon", "Impossible d'enregistrer {0} (utilisé par une autre app)"},
                {"TrayTip", "Sesli Okuma  ·  {0}"},
                {"Ready", "Sesli Okuma {0} est prêt"},
                {"Cancelled", "Annulé"},
            };

            all["ar"] = new Dictionary<string, string> {
                {"Subtitle", "{0}  يقرأ النص المحدد  ·  اضغط مرة أخرى للتوقف"},
                {"Hotkey", "الاختصار"},
                {"HotkeyHint", "انقر: تغيير  ·  مرتان: الحافظة  ·  مطولاً: إيقاف"},
                {"HotkeyCapture", "اضغط على مجموعة المفاتيح الجديدة…  (Esc للإلغاء)"},
                {"HotkeyNeedMod", "اجمع مفتاحاً مع Ctrl أو Alt أو Shift أو Win"},
                {"HotkeyTaken", "{0} مستخدم بالفعل في تطبيق آخر"},
                {"HotkeySaved", "الاختصار: {0}"},
                {"Primary", "اللغة الأساسية"},
                {"Other", "لغات أخرى"},
                {"NoVoice", "لم يتم اختيار صوت"},
                {"PickVoice", "انقر لاختيار صوت"},
                {"Natural", "صوت طبيعي"},
                {"Classic", "صوت كلاسيكي"},
                {"Multilingual", "متعدد اللغات"},
                {"Speed", "سرعة القراءة"},
                {"Normal", "عادية"},
                {"Fast", "سريعة"},
                {"Slow", "بطيئة"},
                {"StartWithWindows", "التشغيل مع Windows"},
                {"AutoUpdate", "التحقق من التحديثات تلقائياً"},
                {"Language", "لغة الواجهة"},
                {"Check", "تحقق"},
                {"Checking", "جارٍ التحقق من التحديثات…"},
                {"UpToDate", "محدَّث: {0}"},
                {"CheckFailed", "تعذر التحقق من التحديثات (الشبكة؟)"},
                {"NewVersion", "الإصدار {0} متاح"},
                {"ReleaseNotes", "انقر لملاحظات الإصدار"},
                {"Update", "تحديث"},
                {"Skipped", "تم تخطي هذا الإصدار"},
                {"Downloading", "جارٍ التنزيل…  {0}%"},
                {"Installing", "جارٍ التحقق والتثبيت…"},
                {"RestartSoon", "سيُعاد تشغيل التطبيق خلال ثوانٍ"},
                {"UpdateClick", "انقر للتحديث."},
                {"NaturalTitle", "أصوات طبيعية تشبه البشر"},
                {"NaturalText", "أضف أصواتاً عصبية مجانية للعديد من اللغات"},
                {"Install", "تثبيت"},
                {"NaturalInstalling", "جارٍ تثبيت الأصوات الطبيعية…  {0}%"},
                {"NaturalRegistering", "جارٍ تسجيل الأصوات (موافقة المسؤول)…"},
                {"NaturalDone", "تمت إضافة الأصوات الطبيعية"},
                {"NaturalFailed", "لم يكتمل التثبيت: {0}"},
                {"NaturalNote", "NaturalVoiceSAPIAdapter مفتوح المصدر · أصوات Microsoft عبر الإنترنت · يتطلب اتصالاً"},
                {"MoreVoices", "إضافة حزم أصوات Windows…"},
                {"Settings", "الإعدادات"},
                {"Stop", "إيقاف"},
                {"CheckUpdates", "التحقق من التحديثات"},
                {"Exit", "خروج"},
                {"VoiceSelected", "تم اختيار {0}"},
                {"StartupAdded", "تمت الإضافة إلى بدء التشغيل"},
                {"StartupRemoved", "تمت الإزالة من بدء التشغيل"},
                {"HotkeyFailBalloon", "تعذر تسجيل {0} (مستخدم في تطبيق آخر)"},
                {"TrayTip", "Sesli Okuma  ·  {0}"},
                {"Ready", "Sesli Okuma {0} جاهز"},
                {"Cancelled", "أُلغي"},
            };

            all["pt"] = new Dictionary<string, string> {
                {"Subtitle", "{0}  lê o texto selecionado  ·  pressione de novo para parar"},
                {"Hotkey", "ATALHO"},
                {"HotkeyHint", "Clique: alterar  ·  2×: área de transf.  ·  segurar: pausar"},
                {"HotkeyCapture", "Pressione a nova combinação de teclas…  (Esc cancela)"},
                {"HotkeyNeedMod", "Combine uma tecla com Ctrl, Alt, Shift ou Win"},
                {"HotkeyTaken", "{0} já é usado por outro aplicativo"},
                {"HotkeySaved", "Atalho: {0}"},
                {"Primary", "IDIOMA PRINCIPAL"},
                {"Other", "OUTROS IDIOMAS"},
                {"NoVoice", "Nenhuma voz selecionada"},
                {"PickVoice", "Clique para escolher uma voz"},
                {"Natural", "voz natural"},
                {"Classic", "voz clássica"},
                {"Multilingual", "multilíngue"},
                {"Speed", "VELOCIDADE DE LEITURA"},
                {"Normal", "Normal"},
                {"Fast", "Rápida"},
                {"Slow", "Lenta"},
                {"StartWithWindows", "Iniciar com o Windows"},
                {"AutoUpdate", "Verificar atualizações automaticamente"},
                {"Language", "Idioma da interface"},
                {"Check", "verificar"},
                {"Checking", "Verificando atualizações…"},
                {"UpToDate", "Atualizado: {0}"},
                {"CheckFailed", "Não foi possível verificar (rede?)"},
                {"NewVersion", "Versão {0} disponível"},
                {"ReleaseNotes", "Clique para ver as novidades"},
                {"Update", "Atualizar"},
                {"Skipped", "Versão ignorada"},
                {"Downloading", "Baixando…  {0}%"},
                {"Installing", "Verificando e instalando…"},
                {"RestartSoon", "O aplicativo será reiniciado em alguns segundos"},
                {"UpdateClick", "Clique para atualizar."},
                {"NaturalTitle", "Vozes naturais, quase humanas"},
                {"NaturalText", "Adicione vozes neurais gratuitas para muitos idiomas"},
                {"Install", "Instalar"},
                {"NaturalInstalling", "Instalando vozes naturais…  {0}%"},
                {"NaturalRegistering", "Registrando vozes (permissão de administrador)…"},
                {"NaturalDone", "Vozes naturais adicionadas"},
                {"NaturalFailed", "A instalação não foi concluída: {0}"},
                {"NaturalNote", "NaturalVoiceSAPIAdapter de código aberto · vozes online da Microsoft · requer internet"},
                {"MoreVoices", "Adicionar pacotes de voz do Windows…"},
                {"Settings", "Configurações"},
                {"Stop", "Parar"},
                {"CheckUpdates", "Verificar atualizações"},
                {"Exit", "Sair"},
                {"VoiceSelected", "{0} selecionada"},
                {"StartupAdded", "Adicionado à inicialização"},
                {"StartupRemoved", "Removido da inicialização"},
                {"HotkeyFailBalloon", "Não foi possível registrar {0} (usado por outro app)"},
                {"TrayTip", "Sesli Okuma  ·  {0}"},
                {"Ready", "Sesli Okuma {0} está pronto"},
                {"Cancelled", "Cancelado"},
            };

            AddAbout(all);
            return all;
        }

        static void Put(Dictionary<string, Dictionary<string, string>> all, string lang, string about, string tagline, string source, string notes, string issue, string credits, string support, string supportNote)
        {
            var d = all[lang];
            d["About"] = about; d["Tagline"] = tagline; d["SourceCode"] = source; d["ReleaseNotes2"] = notes; d["ReportIssue"] = issue;
            d["Credits"] = credits; d["Support"] = support; d["SupportNote"] = supportNote;
        }

        static void AddAbout(Dictionary<string, Dictionary<string, string>> all)
        {
            Put(all, "tr", "Hakkında", "Herhangi bir uygulamada seçtiğiniz metni tek kısayolla sesli okuyan ücretsiz, açık kaynak bir Windows aracı.", "Kaynak kod", "Sürüm notları", "Sorun bildir / öneri", "Doğal sesler için kullanılan proje", "Destek ol", "Tek seferlik, karşılıksız bağış");
            Put(all, "en", "About", "A free, open-source Windows tool that reads the text you select in any app with a single shortcut.", "Source code", "Release notes", "Report an issue / suggest", "Project used for natural voices", "Support the project", "One-time, no strings attached");
            Put(all, "zh", "关于", "一款免费开源的 Windows 工具，只需一个快捷键即可朗读任意应用中选中的文字。", "源代码", "更新说明", "报告问题 / 建议", "自然语音所用项目", "支持本项目", "一次性、无附加条件的捐助");
            Put(all, "hi", "परिचय", "एक मुफ़्त, ओपन-सोर्स Windows टूल जो किसी भी ऐप में चुने गए पाठ को एक शॉर्टकट से पढ़ता है।", "स्रोत कोड", "रिलीज़ नोट्स", "समस्या बताएँ / सुझाव", "प्राकृतिक आवाज़ों के लिए प्रयुक्त प्रोजेक्ट", "प्रोजेक्ट का समर्थन करें", "एक बार, बिना किसी शर्त");
            Put(all, "es", "Acerca de", "Una herramienta gratuita y de código abierto para Windows que lee el texto seleccionado en cualquier aplicación con un solo atajo.", "Código fuente", "Notas de la versión", "Informar un problema / sugerir", "Proyecto usado para las voces naturales", "Apoyar el proyecto", "Donación única, sin compromisos");
            Put(all, "fr", "À propos", "Un outil Windows gratuit et open source qui lit le texte sélectionné dans n'importe quelle application avec un seul raccourci.", "Code source", "Notes de version", "Signaler un problème / suggérer", "Projet utilisé pour les voix naturelles", "Soutenir le projet", "Don unique, sans contrepartie");
            Put(all, "ar", "حول", "أداة Windows مجانية ومفتوحة المصدر تقرأ النص الذي تحدده في أي تطبيق باختصار واحد.", "الشيفرة المصدرية", "ملاحظات الإصدار", "الإبلاغ عن مشكلة / اقتراح", "المشروع المستخدم للأصوات الطبيعية", "ادعم المشروع", "تبرع لمرة واحدة دون شروط");
            Put(all, "pt", "Sobre", "Uma ferramenta gratuita e de código aberto para Windows que lê o texto selecionado em qualquer aplicativo com um único atalho.", "Código-fonte", "Notas da versão", "Relatar um problema / sugerir", "Projeto usado para as vozes naturais", "Apoiar o projeto", "Doação única, sem compromissos");
            AddWindowsVoice(all);
            AddReaderBar(all);
            AddSave(all);
            AddTranslate(all);
            AddHintSimple(all);
            AddUi(all);
            AddFreeTranslate(all);
            AddMore(all);
            AddBar2(all);
        }

        static void Put2(Dictionary<string, Dictionary<string, string>> all, string lang, string install, string installing, string done, string failed, string noVoice, string settings)
        {
            var d = all[lang];
            d["WinVoiceInstall"] = install; d["WinVoiceInstalling"] = installing; d["WinVoiceDone"] = done; d["WinVoiceFailed"] = failed; d["NoVoiceForLang"] = noVoice; d["WinVoiceSettings"] = settings;
        }

        static void AddSave(Dictionary<string, Dictionary<string, string>> all)
        {
            all["zh"]["SaveAudio"] = "将选中文字保存为音频文件"; all["zh"]["NoTextToSave"] = "没有可保存的文字（请先选择文字）"; all["zh"]["SavedTo"] = "已保存音频文件：{0}"; all["zh"]["SaveFailed"] = "无法保存：{0}";
            all["tr"]["SaveAudio"] = "Seçimi ses dosyasına kaydet"; all["tr"]["NoTextToSave"] = "Kaydedilecek metin bulunamadı (önce bir metin seçin)"; all["tr"]["SavedTo"] = "Ses dosyası kaydedildi: {0}"; all["tr"]["SaveFailed"] = "Kaydedilemedi: {0}";
            all["ar"]["SaveAudio"] = "حفظ التحديد كملف صوتي"; all["ar"]["NoTextToSave"] = "لا يوجد نص للحفظ (حدد نصاً أولاً)"; all["ar"]["SavedTo"] = "تم حفظ الملف الصوتي: {0}"; all["ar"]["SaveFailed"] = "تعذر الحفظ: {0}";
            all["es"]["SaveAudio"] = "Guardar selección como archivo de audio"; all["es"]["NoTextToSave"] = "No hay texto para guardar (selecciona texto primero)"; all["es"]["SavedTo"] = "Archivo de audio guardado: {0}"; all["es"]["SaveFailed"] = "No se pudo guardar: {0}";
            all["en"]["SaveAudio"] = "Save selection as audio file"; all["en"]["NoTextToSave"] = "No text to save (select some text first)"; all["en"]["SavedTo"] = "Audio file saved: {0}"; all["en"]["SaveFailed"] = "Could not save: {0}";
            all["hi"]["SaveAudio"] = "चयन को ऑडियो फ़ाइल के रूप में सहेजें"; all["hi"]["NoTextToSave"] = "सहेजने के लिए पाठ नहीं (पहले पाठ चुनें)"; all["hi"]["SavedTo"] = "ऑडियो फ़ाइल सहेजी गई: {0}"; all["hi"]["SaveFailed"] = "सहेजा नहीं जा सका: {0}";
            all["pt"]["SaveAudio"] = "Salvar seleção como arquivo de áudio"; all["pt"]["NoTextToSave"] = "Nenhum texto para salvar (selecione um texto primeiro)"; all["pt"]["SavedTo"] = "Arquivo de áudio salvo: {0}"; all["pt"]["SaveFailed"] = "Não foi possível salvar: {0}";
            all["fr"]["SaveAudio"] = "Enregistrer la sélection en fichier audio"; all["fr"]["NoTextToSave"] = "Aucun texte à enregistrer (sélectionnez du texte)"; all["fr"]["SavedTo"] = "Fichier audio enregistré : {0}"; all["fr"]["SaveFailed"] = "Enregistrement impossible : {0}";
        }

        static void AddTranslate(Dictionary<string, Dictionary<string, string>> all)
        {
            all["es"]["Advanced"] = "Avanzado";
            all["es"]["TranslateHotkey"] = "ATAJO TRADUCIR Y LEER";
            all["es"]["DeepLKey"] = "CLAVE DEEPL (OPCIONAL)";
            all["es"]["GetKey"] = "Obtener clave gratis →";
            all["es"]["TranslateNeedsKey"] = "La traducción necesita una clave DeepL — abre Avanzado en el panel";
            all["es"]["Translating"] = "Traduciendo…";
            all["es"]["TranslateFailed"] = "Error de traducción: {0}";
            all["es"]["TranslateBadKey"] = "Clave DeepL no válida";
            all["es"]["TranslateQuota"] = "Cuota mensual de DeepL agotada";
            all["es"]["TranslateUnsupported"] = "DeepL no admite {0}";
            all["es"]["TranslateRead"] = "Traducir y leer";
            all["es"]["KeySaved"] = "Clave guardada";
            all["ar"]["Advanced"] = "متقدم";
            all["ar"]["TranslateHotkey"] = "اختصار الترجمة والقراءة";
            all["ar"]["DeepLKey"] = "مفتاح DEEPL (اختياري)";
            all["ar"]["GetKey"] = "احصل على مفتاح مجاني →";
            all["ar"]["TranslateNeedsKey"] = "الترجمة تحتاج مفتاح DeepL — افتح «متقدم» في اللوحة";
            all["ar"]["Translating"] = "جارٍ الترجمة…";
            all["ar"]["TranslateFailed"] = "فشلت الترجمة: {0}";
            all["ar"]["TranslateBadKey"] = "مفتاح DeepL غير صالح";
            all["ar"]["TranslateQuota"] = "انتهت حصة DeepL الشهرية";
            all["ar"]["TranslateUnsupported"] = "DeepL لا يدعم {0}";
            all["ar"]["TranslateRead"] = "ترجمة وقراءة";
            all["ar"]["KeySaved"] = "تم حفظ المفتاح";
            all["zh"]["Advanced"] = "高级";
            all["zh"]["TranslateHotkey"] = "翻译并朗读快捷键";
            all["zh"]["DeepLKey"] = "DEEPL 密钥（可选）";
            all["zh"]["GetKey"] = "获取免费密钥 →";
            all["zh"]["TranslateNeedsKey"] = "翻译需要 DeepL 密钥 — 请在面板中打开“高级”";
            all["zh"]["Translating"] = "正在翻译…";
            all["zh"]["TranslateFailed"] = "翻译失败：{0}";
            all["zh"]["TranslateBadKey"] = "DeepL 密钥无效";
            all["zh"]["TranslateQuota"] = "DeepL 月度配额已用完";
            all["zh"]["TranslateUnsupported"] = "DeepL 不支持 {0}";
            all["zh"]["TranslateRead"] = "翻译并朗读";
            all["zh"]["KeySaved"] = "密钥已保存";
            all["en"]["Advanced"] = "Advanced";
            all["en"]["TranslateHotkey"] = "TRANSLATE & READ SHORTCUT";
            all["en"]["DeepLKey"] = "DEEPL KEY (OPTIONAL)";
            all["en"]["GetKey"] = "Get a free key →";
            all["en"]["TranslateNeedsKey"] = "Translation needs a DeepL key — open Advanced in the panel";
            all["en"]["Translating"] = "Translating…";
            all["en"]["TranslateFailed"] = "Translation failed: {0}";
            all["en"]["TranslateBadKey"] = "Invalid DeepL key";
            all["en"]["TranslateQuota"] = "DeepL monthly quota exhausted";
            all["en"]["TranslateUnsupported"] = "DeepL does not support {0}";
            all["en"]["TranslateRead"] = "Translate & read";
            all["en"]["KeySaved"] = "Key saved";
            all["fr"]["Advanced"] = "Avancé";
            all["fr"]["TranslateHotkey"] = "RACCOURCI TRADUIRE ET LIRE";
            all["fr"]["DeepLKey"] = "CLÉ DEEPL (FACULTATIVE)";
            all["fr"]["GetKey"] = "Obtenir une clé gratuite →";
            all["fr"]["TranslateNeedsKey"] = "La traduction nécessite une clé DeepL — ouvrez Avancé dans le panneau";
            all["fr"]["Translating"] = "Traduction…";
            all["fr"]["TranslateFailed"] = "Échec de la traduction : {0}";
            all["fr"]["TranslateBadKey"] = "Clé DeepL invalide";
            all["fr"]["TranslateQuota"] = "Quota mensuel DeepL épuisé";
            all["fr"]["TranslateUnsupported"] = "DeepL ne prend pas en charge {0}";
            all["fr"]["TranslateRead"] = "Traduire et lire";
            all["fr"]["KeySaved"] = "Clé enregistrée";
            all["hi"]["Advanced"] = "उन्नत";
            all["hi"]["TranslateHotkey"] = "अनुवाद करें और पढ़ें शॉर्टकट";
            all["hi"]["DeepLKey"] = "DEEPL कुंजी (वैकल्पिक)";
            all["hi"]["GetKey"] = "मुफ़्त कुंजी प्राप्त करें →";
            all["hi"]["TranslateNeedsKey"] = "अनुवाद के लिए DeepL कुंजी चाहिए — पैनल में उन्नत खोलें";
            all["hi"]["Translating"] = "अनुवाद हो रहा है…";
            all["hi"]["TranslateFailed"] = "अनुवाद विफल: {0}";
            all["hi"]["TranslateBadKey"] = "DeepL कुंजी अमान्य";
            all["hi"]["TranslateQuota"] = "DeepL मासिक कोटा समाप्त";
            all["hi"]["TranslateUnsupported"] = "DeepL {0} का समर्थन नहीं करता";
            all["hi"]["TranslateRead"] = "अनुवाद करें और पढ़ें";
            all["hi"]["KeySaved"] = "कुंजी सहेजी गई";
            all["tr"]["Advanced"] = "Gelişmiş";
            all["tr"]["TranslateHotkey"] = "ÇEVİR VE OKU KISAYOLU";
            all["tr"]["DeepLKey"] = "DEEPL ANAHTARI (İSTEĞE BAĞLI)";
            all["tr"]["GetKey"] = "Ücretsiz anahtar al →";
            all["tr"]["TranslateNeedsKey"] = "Çeviri için DeepL anahtarı gerekir — panelde Gelişmiş bölümüne girin";
            all["tr"]["Translating"] = "Çevriliyor…";
            all["tr"]["TranslateFailed"] = "Çeviri başarısız: {0}";
            all["tr"]["TranslateBadKey"] = "DeepL anahtarı geçersiz";
            all["tr"]["TranslateQuota"] = "DeepL aylık kotası doldu";
            all["tr"]["TranslateUnsupported"] = "DeepL {0} dilini desteklemiyor";
            all["tr"]["TranslateRead"] = "Çevir ve oku";
            all["tr"]["KeySaved"] = "Anahtar kaydedildi";
            all["pt"]["Advanced"] = "Avançado";
            all["pt"]["TranslateHotkey"] = "ATALHO TRADUZIR E LER";
            all["pt"]["DeepLKey"] = "CHAVE DEEPL (OPCIONAL)";
            all["pt"]["GetKey"] = "Obter chave grátis →";
            all["pt"]["TranslateNeedsKey"] = "A tradução precisa de uma chave DeepL — abra Avançado no painel";
            all["pt"]["Translating"] = "Traduzindo…";
            all["pt"]["TranslateFailed"] = "Falha na tradução: {0}";
            all["pt"]["TranslateBadKey"] = "Chave DeepL inválida";
            all["pt"]["TranslateQuota"] = "Cota mensal do DeepL esgotada";
            all["pt"]["TranslateUnsupported"] = "O DeepL não suporta {0}";
            all["pt"]["TranslateRead"] = "Traduzir e ler";
            all["pt"]["KeySaved"] = "Chave salva";
        }

        static void AddHintSimple(Dictionary<string, Dictionary<string, string>> all)
        {
            all["es"]["HotkeyHintSimple"] = "Clic: cambiar";
            all["ar"]["HotkeyHintSimple"] = "انقر: تغيير";
            all["tr"]["HotkeyHintSimple"] = "Tıkla: değiştir";
            all["pt"]["HotkeyHintSimple"] = "Clique: alterar";
            all["en"]["HotkeyHintSimple"] = "Click: change";
            all["fr"]["HotkeyHintSimple"] = "Clic : modifier";
            all["hi"]["HotkeyHintSimple"] = "क्लिक: बदलें";
            all["zh"]["HotkeyHintSimple"] = "点击：更改";
        }

        static void AddUi(Dictionary<string, Dictionary<string, string>> all)
        {
            all["en"]["SlightlySlow"] = "Slightly slow";
            all["en"]["SlightlyFast"] = "Slightly fast";
            all["en"]["Listen"] = "Listen";
            all["en"]["Close"] = "Close";
            all["en"]["Copy"] = "Copy";
            all["en"]["SkipVersionTip"] = "Skip this version";
            all["en"]["Previous"] = "Previous sentence";
            all["en"]["Next"] = "Next sentence";
            all["en"]["Pause"] = "Pause";
            all["en"]["Resume"] = "Resume";
            all["en"]["SpeedTip"] = "Speed";
            all["hi"]["SlightlySlow"] = "थोड़ा धीमा";
            all["hi"]["SlightlyFast"] = "थोड़ा तेज़";
            all["hi"]["Listen"] = "सुनें";
            all["hi"]["Close"] = "बंद करें";
            all["hi"]["Copy"] = "कॉपी";
            all["hi"]["SkipVersionTip"] = "यह संस्करण छोड़ें";
            all["hi"]["Previous"] = "पिछला वाक्य";
            all["hi"]["Next"] = "अगला वाक्य";
            all["hi"]["Pause"] = "रोकें";
            all["hi"]["Resume"] = "जारी रखें";
            all["hi"]["SpeedTip"] = "गति";
            all["fr"]["SlightlySlow"] = "Un peu lent";
            all["fr"]["SlightlyFast"] = "Un peu rapide";
            all["fr"]["Listen"] = "Écouter";
            all["fr"]["Close"] = "Fermer";
            all["fr"]["Copy"] = "Copier";
            all["fr"]["SkipVersionTip"] = "Ignorer cette version";
            all["fr"]["Previous"] = "Phrase précédente";
            all["fr"]["Next"] = "Phrase suivante";
            all["fr"]["Pause"] = "Pause";
            all["fr"]["Resume"] = "Reprendre";
            all["fr"]["SpeedTip"] = "Vitesse";
            all["tr"]["SlightlySlow"] = "Biraz yavaş";
            all["tr"]["SlightlyFast"] = "Biraz hızlı";
            all["tr"]["Listen"] = "Dinle";
            all["tr"]["Close"] = "Kapat";
            all["tr"]["Copy"] = "Kopyala";
            all["tr"]["SkipVersionTip"] = "Bu sürümü geç";
            all["tr"]["Previous"] = "Önceki cümle";
            all["tr"]["Next"] = "Sonraki cümle";
            all["tr"]["Pause"] = "Duraklat";
            all["tr"]["Resume"] = "Devam";
            all["tr"]["SpeedTip"] = "Hız";
            all["ar"]["SlightlySlow"] = "أبطأ قليلاً";
            all["ar"]["SlightlyFast"] = "أسرع قليلاً";
            all["ar"]["Listen"] = "استماع";
            all["ar"]["Close"] = "إغلاق";
            all["ar"]["Copy"] = "نسخ";
            all["ar"]["SkipVersionTip"] = "تخطي هذا الإصدار";
            all["ar"]["Previous"] = "الجملة السابقة";
            all["ar"]["Next"] = "الجملة التالية";
            all["ar"]["Pause"] = "إيقاف مؤقت";
            all["ar"]["Resume"] = "متابعة";
            all["ar"]["SpeedTip"] = "السرعة";
            all["es"]["SlightlySlow"] = "Algo lento";
            all["es"]["SlightlyFast"] = "Algo rápido";
            all["es"]["Listen"] = "Escuchar";
            all["es"]["Close"] = "Cerrar";
            all["es"]["Copy"] = "Copiar";
            all["es"]["SkipVersionTip"] = "Omitir esta versión";
            all["es"]["Previous"] = "Frase anterior";
            all["es"]["Next"] = "Frase siguiente";
            all["es"]["Pause"] = "Pausa";
            all["es"]["Resume"] = "Continuar";
            all["es"]["SpeedTip"] = "Velocidad";
            all["pt"]["SlightlySlow"] = "Um pouco lento";
            all["pt"]["SlightlyFast"] = "Um pouco rápido";
            all["pt"]["Listen"] = "Ouvir";
            all["pt"]["Close"] = "Fechar";
            all["pt"]["Copy"] = "Copiar";
            all["pt"]["SkipVersionTip"] = "Ignorar esta versão";
            all["pt"]["Previous"] = "Frase anterior";
            all["pt"]["Next"] = "Próxima frase";
            all["pt"]["Pause"] = "Pausar";
            all["pt"]["Resume"] = "Continuar";
            all["pt"]["SpeedTip"] = "Velocidade";
            all["zh"]["SlightlySlow"] = "稍慢";
            all["zh"]["SlightlyFast"] = "稍快";
            all["zh"]["Listen"] = "试听";
            all["zh"]["Close"] = "关闭";
            all["zh"]["Copy"] = "复制";
            all["zh"]["SkipVersionTip"] = "跳过此版本";
            all["zh"]["Previous"] = "上一句";
            all["zh"]["Next"] = "下一句";
            all["zh"]["Pause"] = "暂停";
            all["zh"]["Resume"] = "继续";
            all["zh"]["SpeedTip"] = "速度";
        }

        static void AddFreeTranslate(Dictionary<string, Dictionary<string, string>> all)
        {
            all["fr"]["DeepLKey"] = "CLÉ DEEPL (FACULTATIVE)"; all["fr"]["FreeQuota"] = "La limite de traduction gratuite du jour est atteinte (environ 5 000 caractères)"; all["fr"]["FreeEngine"] = "Traduction gratuite (MyMemory) · sans clé";
            all["zh"]["DeepLKey"] = "DEEPL 密钥（可选）"; all["zh"]["FreeQuota"] = "今日免费翻译额度已用完（约 5,000 字符）"; all["zh"]["FreeEngine"] = "免费翻译（MyMemory）· 无需密钥";
            all["ar"]["DeepLKey"] = "مفتاح DEEPL (اختياري)"; all["ar"]["FreeQuota"] = "انتهى حد الترجمة المجانية لهذا اليوم (نحو 5000 حرف)"; all["ar"]["FreeEngine"] = "ترجمة مجانية (MyMemory) · بدون مفتاح";
            all["pt"]["DeepLKey"] = "CHAVE DEEPL (OPCIONAL)"; all["pt"]["FreeQuota"] = "O limite gratuito de tradução de hoje acabou (cerca de 5.000 caracteres)"; all["pt"]["FreeEngine"] = "Tradução gratuita (MyMemory) · sem chave";
            all["tr"]["DeepLKey"] = "DEEPL ANAHTARI (İSTEĞE BAĞLI)"; all["tr"]["FreeQuota"] = "Bugünlük ücretsiz çeviri sınırı doldu (yaklaşık 5.000 karakter)"; all["tr"]["FreeEngine"] = "Ücretsiz çeviri (MyMemory) · anahtar gerekmez";
            all["hi"]["DeepLKey"] = "DEEPL कुंजी (वैकल्पिक)"; all["hi"]["FreeQuota"] = "आज की मुफ़्त अनुवाद सीमा समाप्त (लगभग 5,000 अक्षर)"; all["hi"]["FreeEngine"] = "मुफ़्त अनुवाद (MyMemory) · कुंजी की ज़रूरत नहीं";
            all["en"]["DeepLKey"] = "DEEPL KEY (OPTIONAL)"; all["en"]["FreeQuota"] = "Today's free translation limit is used up (about 5,000 characters)"; all["en"]["FreeEngine"] = "Free translation (MyMemory) · no key needed";
            all["es"]["DeepLKey"] = "CLAVE DEEPL (OPCIONAL)"; all["es"]["FreeQuota"] = "Se agotó el límite de traducción gratuita de hoy (unos 5.000 caracteres)"; all["es"]["FreeEngine"] = "Traducción gratuita (MyMemory) · sin clave";
        }

        static void AddMore(Dictionary<string, Dictionary<string, string>> all)
        {
            all["fr"]["HoverRead"] = "Lire ce que la souris pointe (accessibilité)"; all["fr"]["Stats"] = "Aujourd'hui {0} mots  ·  total {1}"; all["fr"]["ContextMenu"] = "Lire avec Sesli Okuma";
            all["tr"]["HoverRead"] = "Fareyle üzerine gelince oku (erişilebilirlik)"; all["tr"]["Stats"] = "Bugün {0} kelime  ·  toplam {1}"; all["tr"]["ContextMenu"] = "Sesli Okuma ile oku";
            all["ar"]["HoverRead"] = "قراءة ما يشير إليه الفأرة (إتاحة)"; all["ar"]["Stats"] = "اليوم {0} كلمة  ·  المجموع {1}"; all["ar"]["ContextMenu"] = "قراءة باستخدام Sesli Okuma";
            all["hi"]["HoverRead"] = "माउस जिस पर हो उसे पढ़ें (सुलभता)"; all["hi"]["Stats"] = "आज {0} शब्द  ·  कुल {1}"; all["hi"]["ContextMenu"] = "Sesli Okuma से पढ़ें";
            all["en"]["HoverRead"] = "Read what the mouse points at (accessibility)"; all["en"]["Stats"] = "Today {0} words  ·  total {1}"; all["en"]["ContextMenu"] = "Read with Sesli Okuma";
            all["es"]["HoverRead"] = "Leer lo que señala el ratón (accesibilidad)"; all["es"]["Stats"] = "Hoy {0} palabras  ·  total {1}"; all["es"]["ContextMenu"] = "Leer con Sesli Okuma";
            all["pt"]["HoverRead"] = "Ler o que o mouse aponta (acessibilidade)"; all["pt"]["Stats"] = "Hoje {0} palavras  ·  total {1}"; all["pt"]["ContextMenu"] = "Ler com Sesli Okuma";
            all["zh"]["HoverRead"] = "朗读鼠标指向的内容（无障碍）"; all["zh"]["Stats"] = "今天 {0} 词  ·  总计 {1}"; all["zh"]["ContextMenu"] = "用 Sesli Okuma 朗读";
        }

        static void AddBar2(Dictionary<string, Dictionary<string, string>> all)
        {
            all["tr"]["ReadingNow"] = "Okuma sürüyor"; all["tr"]["ShowBar"] = "Göster"; all["tr"]["ShowBarMenu"] = "Mini çubuğu göster"; all["tr"]["HideBar"] = "Gizle (okuma sürer)"; all["tr"]["ExpandTip"] = "Tam cümleyi göster"; all["tr"]["CollapseTip"] = "Daralt";
            all["ar"]["ReadingNow"] = "القراءة جارية"; all["ar"]["ShowBar"] = "إظهار"; all["ar"]["ShowBarMenu"] = "إظهار الشريط الصغير"; all["ar"]["HideBar"] = "إخفاء (تستمر القراءة)"; all["ar"]["ExpandTip"] = "إظهار الجملة كاملة"; all["ar"]["CollapseTip"] = "طيّ";
            all["en"]["ReadingNow"] = "Reading in progress"; all["en"]["ShowBar"] = "Show"; all["en"]["ShowBarMenu"] = "Show the mini bar"; all["en"]["HideBar"] = "Hide (keeps reading)"; all["en"]["ExpandTip"] = "Show the full sentence"; all["en"]["CollapseTip"] = "Collapse";
            all["pt"]["ReadingNow"] = "Leitura em andamento"; all["pt"]["ShowBar"] = "Mostrar"; all["pt"]["ShowBarMenu"] = "Mostrar a barra mini"; all["pt"]["HideBar"] = "Ocultar (continua lendo)"; all["pt"]["ExpandTip"] = "Mostrar a frase completa"; all["pt"]["CollapseTip"] = "Recolher";
            all["hi"]["ReadingNow"] = "पढ़ना जारी है"; all["hi"]["ShowBar"] = "दिखाएँ"; all["hi"]["ShowBarMenu"] = "मिनी बार दिखाएँ"; all["hi"]["HideBar"] = "छिपाएँ (पढ़ना जारी रहेगा)"; all["hi"]["ExpandTip"] = "पूरा वाक्य दिखाएँ"; all["hi"]["CollapseTip"] = "समेटें";
            all["zh"]["ReadingNow"] = "正在朗读"; all["zh"]["ShowBar"] = "显示"; all["zh"]["ShowBarMenu"] = "显示迷你工具条"; all["zh"]["HideBar"] = "隐藏（继续朗读）"; all["zh"]["ExpandTip"] = "显示完整句子"; all["zh"]["CollapseTip"] = "收起";
            all["fr"]["ReadingNow"] = "Lecture en cours"; all["fr"]["ShowBar"] = "Afficher"; all["fr"]["ShowBarMenu"] = "Afficher la mini-barre"; all["fr"]["HideBar"] = "Masquer (la lecture continue)"; all["fr"]["ExpandTip"] = "Afficher la phrase complète"; all["fr"]["CollapseTip"] = "Réduire";
            all["es"]["ReadingNow"] = "Lectura en curso"; all["es"]["ShowBar"] = "Mostrar"; all["es"]["ShowBarMenu"] = "Mostrar la barra mini"; all["es"]["HideBar"] = "Ocultar (sigue leyendo)"; all["es"]["ExpandTip"] = "Mostrar la frase completa"; all["es"]["CollapseTip"] = "Contraer";
        }

        static void AddReaderBar(Dictionary<string, Dictionary<string, string>> all)
        {
            all["hi"]["ReaderBar"] = "पढ़ते समय मिनी बार दिखाएँ";
            all["tr"]["ReaderBar"] = "Okurken mini çubuğu göster";
            all["es"]["ReaderBar"] = "Mostrar barra mini al leer";
            all["fr"]["ReaderBar"] = "Afficher la mini-barre pendant la lecture";
            all["en"]["ReaderBar"] = "Show mini bar while reading";
            all["ar"]["ReaderBar"] = "إظهار الشريط الصغير أثناء القراءة";
            all["zh"]["ReaderBar"] = "朗读时显示迷你工具条";
            all["pt"]["ReaderBar"] = "Mostrar barra mini ao ler";
        }

        static void AddWindowsVoice(Dictionary<string, Dictionary<string, string>> all)
        {
            Put2(all, "tr", "Windows sesini ekle ({0})", "Windows sesi kuruluyor — yönetici onayı, birkaç dakika…", "Windows sesi eklendi", "Windows sesi kurulamadı: {0}", "Bu dil için ses yok — Windows sesini eklemek için tıklayın", "Windows ses ayarları…");
            Put2(all, "en", "Add Windows voice ({0})", "Installing Windows voice — admin approval, a few minutes…", "Windows voice added", "Windows voice could not be installed: {0}", "No voice for this language — click to add the Windows voice", "Windows speech settings…");
            Put2(all, "zh", "添加 Windows 语音（{0}）", "正在安装 Windows 语音 — 需要管理员确认，需几分钟…", "已添加 Windows 语音", "无法安装 Windows 语音：{0}", "此语言没有语音 — 点击添加 Windows 语音", "Windows 语音设置…");
            Put2(all, "hi", "Windows आवाज़ जोड़ें ({0})", "Windows आवाज़ इंस्टॉल हो रही है — प्रशासक अनुमति, कुछ मिनट…", "Windows आवाज़ जोड़ी गई", "Windows आवाज़ इंस्टॉल नहीं हो सकी: {0}", "इस भाषा के लिए कोई आवाज़ नहीं — Windows आवाज़ जोड़ने के लिए क्लिक करें", "Windows वाक् सेटिंग्स…");
            Put2(all, "es", "Añadir voz de Windows ({0})", "Instalando la voz de Windows — permiso de administrador, unos minutos…", "Voz de Windows añadida", "No se pudo instalar la voz de Windows: {0}", "No hay voz para este idioma — haz clic para añadir la voz de Windows", "Ajustes de voz de Windows…");
            Put2(all, "fr", "Ajouter la voix Windows ({0})", "Installation de la voix Windows — autorisation administrateur, quelques minutes…", "Voix Windows ajoutée", "Impossible d'installer la voix Windows : {0}", "Aucune voix pour cette langue — cliquez pour ajouter la voix Windows", "Paramètres vocaux de Windows…");
            Put2(all, "ar", "إضافة صوت Windows ({0})", "جارٍ تثبيت صوت Windows — موافقة المسؤول، بضع دقائق…", "تمت إضافة صوت Windows", "تعذر تثبيت صوت Windows: {0}", "لا يوجد صوت لهذه اللغة — انقر لإضافة صوت Windows", "إعدادات الكلام في Windows…");
            Put2(all, "pt", "Adicionar voz do Windows ({0})", "Instalando a voz do Windows — permissão de administrador, alguns minutos…", "Voz do Windows adicionada", "Não foi possível instalar a voz do Windows: {0}", "Nenhuma voz para este idioma — clique para adicionar a voz do Windows", "Configurações de fala do Windows…");
        }
    }
}
