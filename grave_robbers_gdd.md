# GAME DESIGN DOCUMENT — Grave Robbers

Proje Adı (Çalışma Başlığı): Grave Robbers

Geliştirici Ekip: 2 Kişi (1 Senior Generalist/Systems Engineer, 1 Junior Technical Artist/3D Designer)

Hedef Platform: PC (Steam / Co-op)

Oyuncu Sayısı: 2–4 oyuncu online co-op (maksimum 4 bağlantı)

Motor & Araçlar: Unity 6 (URP), Netcode for GameObjects, Blender, FMOD/Unity Audio

Görsel Stil: Low-Poly / Stylized (Hızlı üretilebilir, optimize, eğlenceli)

## Oyunun Kısa Özeti (Elevator Pitch)

Kamyonetinizle ıssız mezarlıklara ve terk edilmiş binalara girip ses çıkarmamaya çalışarak mezar kazdığınız, bulduğunuz fiziksel ve mistik ganimetleri yaratıklardan kaçarken kamyonetin kasına istiflediğiniz, benzin ve risk yönetimi yaparak Safe Zone'da aracınızı geliştirdiğiniz 2–4 kişilik co-op horror-comedy oyunu.

# 0. Multiplayer Kuralları (2–4 Oyuncu)

- Oturum kapasitesi: NetworkManager ConnectionApproval ile max 4 oyuncu. 5. bağlantı reddedilir.
- Spawn: Sahnede 4 SpawnPoint vardır. Server boş slotları sırayla claim eder; disconnect olan oyuncunun slotu serbest kalır.
- Oyuncu ayrımı: Her oyuncu OwnerClientId % 4 ile sabit renge boyanır (mavi / turuncu / yeşil / mor).
- Eşya sahipliği: Loot server-authoritative. Aynı eşyaya eşzamanlı alma isteklerinde server ilk gelen ServerRpc'yi kabul eder, sonrakileri reddeder. Her oyuncu kendi elindeki eşyayı bağımsız taşır.
- Disconnect: Sadece disconnect olan oyuncunun tuttuğu eşya yere bırakılır; diğer oyuncuların taşıdığı loot etkilenmez.
- Fizik: Loot fizikleri yalnızca server'da simüle edilir; client'lar alma/bırakma/fırlatma için istek gönderir.

# 1. Core Game Loop (Çekirdek Oyun Döngüsü)

- Safe Zone (Tüccar / Benzinlik): Eşyaları sat → Benzin al & kamyoneti geliştir
- Kamyonetle yola çıkış
- Keşif & yağma bölgesi: Mezar kaz (ses = yaratık riski), bina lootla, benzin/ışık yönetimi
- Kaçış & risk: Eşyaları kamyonet kasasına fiziksel istifleme, sunak kumar, Safe Zone'a dönüş

# 2. Temel Mekanikler (Core Mechanics)

## 2.1. Kamyonet & Mobil Üs

Yakıt yönetimi, fiziksel kasa (Rigidbody loot; envanter yok), motor/nitro, projektör/zırh, depo genişletme.

## 2.2. Mezar Kazma & Ses

Gürültü yarıçapı; hızlı vs sessiz kazı; RNG loot havuzu (%50 değerli, %25 mistik, %25 tuzak).

## 2.3. Bina Lootlama

Dar iç mekanlar, kapı kırma vs maymuncuk. Ağır kasalar ileride 2+ oyuncu birlikte taşıma gerektirebilir (2–4 kişilik ekibe uygun).

## 2.4. Fiziksel Eşya Taşıma (Prototype)

- Pick / Drop / Throw (E / E / LMB).
- Tek elde tek eşya; ağırlığa göre hafif hız düşüşü.
- Tutma: Server kinematic + NetworkObject.TrySetParent(HoldPoint); bırakınca parent kalkar, fizik açılır.

## 2.5. Silah ve Eşya Geçişi

- Oyuncunun birincil silahı vardır. Elde taşınabilir eşya varken ateş edilemez.
- Eşya alınca silah yere düşmez. Sırta veya bele kılıflanır ve oyuncuda kalır; başka oyuncu bu silahı alamaz.
- Eşya bırakılınca (E) veya fırlatılınca (sol tık) silah ele geri gelir.
- El doluyken yazı "E - Bırak" olur. Ateş tuşuna basılırsa kısa süre "Ellerin dolu" görünür. Mermi göstergesi el doluyken gizlenir.
- Nişangah sürekli açıktır ve ortada beyaz bir noktadır. Bakış, yerde duran ve kimsenin tutmadığı taşınabilir bir eşyanın üzerindeyken nokta parlar.

## 2.6. Mistik Eşyalar & Lanetli Sunak

Tek kullanımlık mistik itemler; sunakta %50 iyi / %50 kötü kumar.

## 2.7. Safe Zone & Ekonomi

Loot sat → benzin al → upgrade → daha tehlikeli bölge.

# 3. Ekip Rol Dağılımı & Teknik Mimari

## Senior (Systems / Gameplay / Netcode)

- Araç, pick-drop, ses/yaratık AI, RNG, Safe Zone ekonomi
- Netcode: 2–4 oyuncu, ConnectionApproval, spawn claim, server-auth loot

## Junior (Art / Level / UI)

- Low-poly kamyonet, mezarlık, loot, Safe Zone, UI, Mixamo animasyonlar

# 4. 4 Aylık Yol Haritası

- Ay 1: Greybox — hareket, pick/drop, kamyonet kasası (2–4 oyuncu netcode iskeleti)
- Ay 2: Full loop — benzin, Safe Zone, sunak, sesli yaratık AI
- Ay 3: Polish — VFX/SFX, ragdoll, arıza durumları, vertical slice
- Ay 4: Bugfix, save, Steam Playtest

# 5. Risk & Kapsam

- Karmaşık yaratık AI yok — sese yürü, görünce koş.
- Envanter UI yok — eşya elde veya kasada.
- Max 4 oyuncu sabit; 5. slot tasarlanmaz.
- Hazır asset / Mixamo serbest.

## Değişiklik Notu

Bu sürüm GDD'yi 2 kişilik co-op varsayımından 2–4 oyuncu online co-op'a günceller. Prototype sahnedeki max bağlantı, spawn claim, loot race ve ClientId renk kuralları kanonik kabul edilir.
