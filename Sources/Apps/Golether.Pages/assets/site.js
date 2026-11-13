// The project page: the texts of every language, the language picker and the download buttons. English is written
// in the HTML itself, so the page reads fine without scripts; the other languages are tables here. The list of
// languages, the picker and the links to the documentation live in languages.js, shared with the invitation page.
(function () {
  'use strict';

  var languages = window.GoletherLanguages;

  var texts = {
    de: {
      'page.title': 'Golether: Momente gemeinsam teilen',
      'nav.language': 'Sprache',
      'nav.join': 'So treten Sie bei',
      'nav.host': 'So laden Sie ein',
      'nav.download': 'Herunterladen',
      'nav.try': 'Online ausprobieren',
      'hero.title': 'Momente <em>gemeinsam</em> teilen,<br>wo immer Sie sind',
      'hero.lead': '<span>Filme und Fotos: dasselbe Bild für alle zugleich, selbst Tausende Kilometer entfernt.</span> <span>Kameras, Stimmen, Reaktionen und Zeichnen auf dem Bild, alles in einem Fenster.</span>',
      'hero.download': 'Golether herunterladen',
      'hero.how': 'Ich habe eine Einladung',
      'hero.free': 'Kostenlos und quelloffen · Windows, Linux, macOS und im Browser · Android bald',
      'hero.shot': 'Das Golether-Fenster: ein Film im Player, die Teilnehmer und der Chat rechts',
      'hero.sync': 'synchron',
      'uses.eyebrow': 'Für wen',
      'uses.title': 'Nicht nur für Filmabende',
      'uses.sub': 'Immer dann, wenn mehrere Menschen im selben Moment dasselbe sehen sollen.',
      'uses.1.title': 'Freunde und Familie',
      'uses.1.text': 'Ein Film am Abend oder Fotos von der Reise, mit denen, die in einer anderen Stadt leben.',
      'uses.2.title': 'Lernen',
      'uses.2.text': 'Lehrer und Schüler gehen gemeinsam einen Film durch: Jeder wählt eigene Untertitel und Tonspur, und eine wichtige Stelle lässt sich direkt im Bild einkreisen.',
      'uses.3.title': 'Aufnahmen besprechen',
      'uses.3.text': 'Ein Trainer schaut mit dem Team die Aufzeichnung eines Spiels: Pause und Spulen wirken für alle zugleich, und ein Spielzug lässt sich aufs Bild zeichnen.',
      'uses.4.title': 'Arbeit mit Fotos',
      'uses.4.text': 'Ein Fotograf zeigt einem Kunden ein Shooting: gemeinsam durchblättern oder jeder vergrößert ein Detail für sich. Aufnahmeort und Kamera bleiben aus den Kopien heraus.',
      'join.eyebrow': 'Sie wurden eingeladen',
      'join.title': 'Vier Schritte bis zur Sitzung',
      'join.sub': 'Sie brauchen nur Golether auf Ihrem Computer und den Link, den Ihnen jemand geschickt hat.',
      'join.1.title': 'Auf den Link klicken',
      'join.1.text': 'Der Browser fragt, ob Golether geöffnet werden soll. Stimmen Sie zu.',
      'join.2.title': 'Bestätigen',
      'join.2.text': 'Golether zeigt den Namen der Sitzung und wer Sie einlädt. Drücken Sie „Beitreten“.',
      'join.3.title': 'Code vergleichen',
      'join.3.text': 'Sie und der Gastgeber sehen dieselben drei Wörter und eine Zahl. Sagen Sie sie einander laut.',
      'join.4.title': 'Genießen',
      'join.4.text': 'Der Gastgeber lässt Sie herein, und der Film oder die Fotos erscheinen für alle im selben Moment.',
      'join.note': '<b>Der Link hat die App nicht geöffnet?</b> Bitten Sie den Gastgeber um den <b>Schlüssel</b>: Kopieren Sie ihn, öffnen Sie Golether, fügen Sie ihn in das Feld „Einladung“ ein und drücken Sie „Verbinden“. Ein Link gilt 15 Minuten, ein Schlüssel bis zu einer Stunde; mit einer Einladung kann die ganze Runde beitreten.',
      'code.1': 'Ahorn',
      'code.2': 'Fluss',
      'code.3': 'Stein',
      'host.eyebrow': 'Sie laden ein',
      'host.title': 'Zeigen Sie einen Film oder Fotos',
      'host.sub': 'Film und Fotos bleiben auf Ihrem Computer und gehen direkt zu Ihren Gästen.<br>Bis zu fünf Personen schauen gemeinsam.',
      'host.1.title': 'Auswählen, was Sie zeigen',
      'host.1.text': 'Drücken Sie „Sitzung erstellen“ und wählen Sie einen Film, ein Foto oder einen ganzen Ordner. Nichts wird auf einen Server hochgeladen.',
      'host.2.title': 'Einladung schicken',
      'host.2.text': 'Drücken Sie „Einladen“, kopieren Sie den Link und posten Sie ihn in Ihren Gruppenchat. Alle treten mit demselben Link bei.',
      'host.3.title': 'Gäste einlassen',
      'host.3.text': 'Vergleichen Sie den Code mit Ihrem Gast per Sprache, lassen Sie ihn herein und drücken Sie auf Abspielen.',
      'why.eyebrow': 'Warum Golether',
      'why.title': 'Wie auf demselben Sofa',
      'why.sub': 'Alles für einen gemeinsamen Abend passt in ein Fenster.',
      'why.1.title': 'Perfekt synchron',
      'why.1.text': 'Abspielen, Pause und Spulen wirken für alle zugleich, egal wer die Taste drückt.',
      'why.2.title': 'Originalqualität',
      'why.2.text': 'Der Film wird nicht zu einem verschwommenen Stream umkodiert: Alle sehen die Datei, wie sie ist.',
      'why.3.title': 'Gesichter und Stimmen',
      'why.3.text': 'Webcam und Stimme aller neben dem Bild, mit Echounterdrückung und eigener Lautstärke für jede Person.',
      'why.4.title': 'Privat',
      'why.4.text': 'Die Verbindung ist verschlüsselt und läuft direkt zwischen Ihnen. Niemand kommt ohne Zustimmung des Gastgebers herein, und Aufnahmeort und Kamera eines Fotos bleiben standardmäßig bei Ihnen.',
      'why.5.title': 'Eigene Untertitel und Tonspur',
      'why.5.text': 'Jeder wählt Untertitel und Tonspur selbst, unabhängig von den anderen.',
      'why.6.title': 'Für immer kostenlos',
      'why.6.text': 'Offener Quellcode. Keine Konten, keine Werbung, keine Abos.',
      'why.7.title': 'Filme und Fotos',
      'why.7.text': 'Zeigen Sie einen Film oder einen ganzen Ordner mit Fotos. Blättern Sie gemeinsam im Album, oder öffnen Sie ein Foto nur für sich und vergrößern Sie ein Detail.',
      'why.8.title': 'Zeichnen auf dem Bild',
      'why.8.text': 'Kreisen Sie ein Detail direkt im Filmbild oder auf einem Foto ein: Alle sehen die Linie, und jeder zeichnet in seiner eigenen Farbe.',
      'why.9.title': 'Reaktionen und Chat',
      'why.9.text': 'Emojis schweben mit dem Namen des Absenders über das Bild, und der Chat ist gleich daneben.',
      'dl.eyebrow': 'Herunterladen',
      'dl.title': 'Golether holen',
      'dl.sub': 'Dieselbe Version für alle, die gemeinsam schauen.<br>Updates werden in der App angeboten.',
      'dl.windows': 'Ein Installationsprogramm für Windows 10 und neuer. Einladungslinks öffnen die App von selbst.',
      'dl.linux': 'Ein Paket für Ubuntu, Debian und verwandte Systeme: Das System installiert die Komponenten für Video und Kameras gleich mit.',
      'dl.windows.other': 'oder das portable .zip-Archiv',
      'dl.linux.other': 'oder das Archiv .tar.gz',
      'dl.macos.other': 'oder für Intel-Macs',
      'dl.macos': 'Ein Archiv für Macs mit Apple Silicon, macOS 14 und neuer.',
      'dl.android': 'Eine App für Telefone: einer Sitzung vom Computer beitreten oder selbst eine für bis zu drei Personen leiten.',
      'dl.soon': 'Bald verfügbar',
      'dl.online': 'Lieber nichts installieren? <a href="watch/" data-umami-event="try-online" data-umami-event-place="downloads">Probieren Sie Golether online aus</a>, direkt im Browser.',
      'dl.button': 'Herunterladen',
      'dl.version': 'Version ',
      'dl.for': 'Herunterladen für ',
      'foot.license': 'Golether ist freie Software unter der Lizenz GPL-3.0-or-later.',
      'foot.source': 'Quellcode',
      'foot.guide': 'Handbuch',
      'foot.privacy': 'Datenschutz',
      'foot.releases': 'Alle Versionen'
    },
    es: {
      'page.title': 'Golether: compartid momentos juntos',
      'nav.language': 'Idioma',
      'nav.join': 'Cómo unirse',
      'nav.host': 'Cómo invitar',
      'nav.download': 'Descargar',
      'nav.try': 'Probar en línea',
      'hero.title': 'Compartid momentos <em>juntos</em><br>estéis donde estéis',
      'hero.lead': '<span>Películas y fotos: el mismo fotograma a la vez, aunque os separen miles de kilómetros.</span> <span>Cámaras, voz, reacciones y dibujo sobre la imagen, todo en una ventana.</span>',
      'hero.download': 'Descargar Golether',
      'hero.how': 'Tengo una invitación',
      'hero.free': 'Gratis y de código abierto · Windows, Linux, macOS y el navegador · Android pronto',
      'hero.shot': 'La ventana de Golether: una película en el reproductor, los participantes y el chat a la derecha',
      'hero.sync': 'sincronizados',
      'uses.eyebrow': 'Para quién',
      'uses.title': 'No solo para noches de cine',
      'uses.sub': 'Siempre que varias personas necesitan ver lo mismo en el mismo momento.',
      'uses.1.title': 'Amigos y familia',
      'uses.1.text': 'Una película por la noche o las fotos de un viaje, con quienes viven en otra ciudad.',
      'uses.2.title': 'Aprendizaje',
      'uses.2.text': 'Un profesor repasa una película con su alumno: cada uno elige sus subtítulos y su pista de audio, y un momento clave se puede rodear sobre el fotograma.',
      'uses.3.title': 'Análisis de grabaciones',
      'uses.3.text': 'Un entrenador ve con el equipo la grabación de un partido: la pausa y el avance funcionan para todos a la vez, y una jugada se puede dibujar sobre el fotograma.',
      'uses.4.title': 'Trabajo con fotos',
      'uses.4.text': 'Un fotógrafo enseña una sesión a un cliente: la pasáis juntos o cada uno amplía un detalle por su cuenta. El lugar y la cámara no van en las copias.',
      'join.eyebrow': 'Te han invitado',
      'join.title': 'Cuatro pasos hasta la sesión',
      'join.sub': 'Solo necesitas Golether en tu ordenador y el enlace que te ha enviado tu amigo.',
      'join.1.title': 'Haz clic en el enlace',
      'join.1.text': 'El navegador pregunta si quieres abrir Golether. Di que sí.',
      'join.2.title': 'Confirma',
      'join.2.text': 'Golether muestra el nombre de la sesión y quién te invita. Pulsa «Unirse».',
      'join.3.title': 'Compara el código',
      'join.3.text': 'Tú y el anfitrión veis las mismas tres palabras y un número. Decídoslas en voz alta.',
      'join.4.title': 'Disfruta',
      'join.4.text': 'El anfitrión te deja entrar y la película o las fotos aparecen para todos en el mismo momento.',
      'join.note': '<b>¿El enlace no abrió la aplicación?</b> Pide al anfitrión la <b>clave</b>: cópiala, abre Golether, pégala en el campo «Invitación» y pulsa «Conectar». Un enlace vale 15 minutos y una clave hasta una hora; todo el grupo puede unirse con una sola invitación.',
      'code.1': 'arce',
      'code.2': 'río',
      'code.3': 'piedra',
      'host.eyebrow': 'Tú invitas',
      'host.title': 'Muestra una película o fotos',
      'host.sub': 'La película y las fotos se quedan en tu ordenador y van directamente a tus invitados.<br>Hasta cinco personas ven juntas.',
      'host.1.title': 'Elige qué mostrar',
      'host.1.text': 'Pulsa «Crear una sesión» y elige una película, una foto o una carpeta entera. No se sube nada a ningún servidor.',
      'host.2.title': 'Envía la invitación',
      'host.2.text': 'Pulsa «Invitar», copia el enlace y publícalo en tu chat de grupo. Todos se unen con el mismo enlace.',
      'host.3.title': 'Déjalos entrar',
      'host.3.text': 'Compara el código con tu invitado de viva voz, déjalo entrar y dale a reproducir.',
      'why.eyebrow': 'Por qué Golether',
      'why.title': 'Como en el mismo sofá',
      'why.sub': 'Todo lo necesario para una noche juntos cabe en una ventana.',
      'why.1.title': 'Perfectamente sincronizado',
      'why.1.text': 'Reproducir, pausar y rebobinar funcionan para todos a la vez, pulse quien pulse el botón.',
      'why.2.title': 'Calidad original',
      'why.2.text': 'La película no se recodifica en una emisión borrosa: todos ven el archivo tal como es.',
      'why.3.title': 'Caras y voces',
      'why.3.text': 'La cámara y la voz de todos junto a la imagen, con cancelación de eco y un volumen para cada persona.',
      'why.4.title': 'Privado',
      'why.4.text': 'La conexión está cifrada y va directamente entre vosotros. Nadie entra sin la aprobación del anfitrión, y por defecto el lugar y la cámara de una foto se quedan contigo.',
      'why.5.title': 'Tus propios subtítulos y audio',
      'why.5.text': 'Cada uno elige los subtítulos y la pista de audio por su cuenta, sin depender de los demás.',
      'why.6.title': 'Gratis para siempre',
      'why.6.text': 'Código abierto. Sin cuentas, sin anuncios, sin suscripciones.',
      'why.7.title': 'Películas y fotos',
      'why.7.text': 'Muestra una película o una carpeta entera de fotos. Hojead el álbum juntos, o abre una foto solo para ti y amplía un detalle.',
      'why.8.title': 'Dibujo sobre la imagen',
      'why.8.text': 'Rodea un detalle directamente en el fotograma de una película o en una foto: todos ven la línea, y cada persona dibuja en su propio color.',
      'why.9.title': 'Reacciones y chat',
      'why.9.text': 'Los emojis flotan sobre la imagen con el nombre de quien los envía, y el chat está justo al lado.',
      'dl.eyebrow': 'Descargar',
      'dl.title': 'Consigue Golether',
      'dl.sub': 'La misma versión para todos los que ven juntos.<br>Las actualizaciones se ofrecen dentro de la aplicación.',
      'dl.windows': 'Un instalador para Windows 10 y posteriores. Los enlaces de invitación abren la aplicación solos.',
      'dl.linux': 'Un paquete para Ubuntu, Debian y derivados: el sistema instala con él los componentes de vídeo y cámaras.',
      'dl.windows.other': 'o el archivo portátil .zip',
      'dl.linux.other': 'o el archivo .tar.gz',
      'dl.macos.other': 'o para Mac con Intel',
      'dl.macos': 'Un archivo para Mac con Apple silicon, macOS 14 y posteriores.',
      'dl.android': 'Una app para teléfonos: únete a una sesión desde un ordenador o crea la tuya para hasta tres personas.',
      'dl.soon': 'Próximamente',
      'dl.online': '¿Prefieres no instalar nada? <a href="watch/" data-umami-event="try-online" data-umami-event-place="downloads">Prueba Golether en línea</a>, directamente en el navegador.',
      'dl.button': 'Descargar',
      'dl.version': 'versión ',
      'dl.for': 'Descargar para ',
      'foot.license': 'Golether es software libre bajo la licencia GPL-3.0-or-later.',
      'foot.source': 'Código fuente',
      'foot.guide': 'Guía de uso',
      'foot.privacy': 'Privacidad',
      'foot.releases': 'Todas las versiones'
    },
    fr: {
      'page.title': 'Golether : partagez vos moments ensemble',
      'nav.language': 'Langue',
      'nav.join': 'Comment rejoindre',
      'nav.host': 'Comment inviter',
      'nav.download': 'Télécharger',
      'nav.try': 'Essayer en ligne',
      'hero.title': 'Partagez vos moments <em>ensemble</em><br>où que vous soyez',
      'hero.lead': '<span>Films et photos : la même image pour tous à la fois, même à des milliers de kilomètres.</span> <span>Caméras, voix, réactions et dessin sur l’image, le tout dans une seule fenêtre.</span>',
      'hero.download': 'Télécharger Golether',
      'hero.how': 'J’ai une invitation',
      'hero.free': 'Gratuit et open source · Windows, Linux, macOS et le navigateur · Android bientôt',
      'hero.shot': 'La fenêtre de Golether : un film dans le lecteur, les participants et la discussion à droite',
      'hero.sync': 'en synchro',
      'uses.eyebrow': 'Pour qui',
      'uses.title': 'Pas seulement pour les soirées cinéma',
      'uses.sub': 'Chaque fois que plusieurs personnes doivent regarder la même chose au même moment.',
      'uses.1.title': 'Amis et famille',
      'uses.1.text': 'Un film le soir ou les photos d’un voyage, avec ceux qui vivent dans une autre ville.',
      'uses.2.title': 'Apprentissage',
      'uses.2.text': 'Un professeur étudie un film avec son élève : chacun choisit ses sous-titres et sa piste audio, et un moment clé s’entoure directement sur l’image.',
      'uses.3.title': 'Analyse d’enregistrements',
      'uses.3.text': 'Un entraîneur regarde avec l’équipe l’enregistrement d’un match : pause et avance rapide agissent pour tous à la fois, et une action se dessine sur l’image.',
      'uses.4.title': 'Travail sur les photos',
      'uses.4.text': 'Un photographe montre une séance à un client : feuilletez-la ensemble, ou que chacun agrandisse un détail de son côté. Le lieu et l’appareil ne partent pas dans les copies.',
      'join.eyebrow': 'Vous êtes invité',
      'join.title': 'Quatre étapes jusqu’à la séance',
      'join.sub': 'Il vous faut seulement Golether sur votre ordinateur et le lien qu’un ami vous a envoyé.',
      'join.1.title': 'Cliquez sur le lien',
      'join.1.text': 'Le navigateur demande s’il faut ouvrir Golether. Acceptez.',
      'join.2.title': 'Confirmez',
      'join.2.text': 'Golether affiche le nom de la séance et qui vous invite. Appuyez sur « Rejoindre ».',
      'join.3.title': 'Comparez le code',
      'join.3.text': 'Vous et l’hôte voyez les trois mêmes mots et un nombre. Dites-les-vous à voix haute.',
      'join.4.title': 'Profitez',
      'join.4.text': 'L’hôte vous fait entrer, et le film ou les photos apparaissent pour tout le monde au même moment.',
      'join.note': '<b>Le lien n’a pas ouvert l’application ?</b> Demandez plutôt la <b>clé</b> à l’hôte : copiez-la, ouvrez Golether, collez-la dans le champ « Invitation » et appuyez sur « Se connecter ». Un lien est valable 15 minutes, une clé jusqu’à une heure ; toute la bande peut rejoindre avec une seule invitation.',
      'code.1': 'érable',
      'code.2': 'rivière',
      'code.3': 'pierre',
      'host.eyebrow': 'Vous invitez',
      'host.title': 'Montrez un film ou des photos',
      'host.sub': 'Le film et les photos restent sur votre ordinateur et vont directement à vos invités.<br>Jusqu’à cinq personnes regardent ensemble.',
      'host.1.title': 'Choisissez quoi montrer',
      'host.1.text': 'Appuyez sur « Créer une séance » et choisissez un film, une photo ou tout un dossier. Rien n’est envoyé sur un serveur.',
      'host.2.title': 'Envoyez l’invitation',
      'host.2.text': 'Appuyez sur « Inviter », copiez le lien et publiez-le dans votre discussion de groupe. Tout le monde rejoint avec le même lien.',
      'host.3.title': 'Faites-les entrer',
      'host.3.text': 'Comparez le code avec votre invité à voix haute, faites-le entrer et lancez la lecture.',
      'why.eyebrow': 'Pourquoi Golether',
      'why.title': 'Comme sur le même canapé',
      'why.sub': 'Tout ce qu’il faut pour une soirée ensemble tient dans une fenêtre.',
      'why.1.title': 'Parfaitement synchronisé',
      'why.1.text': 'Lecture, pause et retour en arrière valent pour tout le monde à la fois, quel que soit celui qui appuie.',
      'why.2.title': 'Qualité d’origine',
      'why.2.text': 'Le film n’est pas réencodé en un flux flou : chacun regarde le fichier tel qu’il est.',
      'why.3.title': 'Visages et voix',
      'why.3.text': 'La webcam et la voix de chacun à côté de l’image, avec annulation de l’écho et un volume pour chaque personne.',
      'why.4.title': 'Privé',
      'why.4.text': 'La connexion est chiffrée et passe directement entre vous. Personne n’entre sans l’accord de l’hôte, et par défaut le lieu et l’appareil d’une photo restent chez vous.',
      'why.5.title': 'Vos sous-titres et votre audio',
      'why.5.text': 'Chacun choisit ses sous-titres et sa piste audio, indépendamment des autres.',
      'why.6.title': 'Gratuit pour de bon',
      'why.6.text': 'Code ouvert. Pas de compte, pas de publicité, pas d’abonnement.',
      'why.7.title': 'Films et photos',
      'why.7.text': 'Montrez un film ou tout un dossier de photos. Feuilletez l’album ensemble, ou ouvrez une photo pour vous seul et agrandissez un détail.',
      'why.8.title': 'Dessin sur l’image',
      'why.8.text': 'Entourez un détail directement sur l’image d’un film ou sur une photo : tout le monde voit le trait, et chacun dessine dans sa propre couleur.',
      'why.9.title': 'Réactions et discussion',
      'why.9.text': 'Les emoji flottent sur l’image avec le nom de l’expéditeur, et la discussion est juste à côté.',
      'dl.eyebrow': 'Télécharger',
      'dl.title': 'Obtenir Golether',
      'dl.sub': 'La même version pour tous ceux qui regardent ensemble.<br>Les mises à jour sont proposées dans l’application.',
      'dl.windows': 'Un programme d’installation pour Windows 10 et plus récent. Les liens d’invitation ouvrent l’application tout seuls.',
      'dl.linux': 'Un paquet pour Ubuntu, Debian et leurs dérivées : le système installe avec lui les composants vidéo et caméra.',
      'dl.windows.other': 'ou l’archive portable .zip',
      'dl.linux.other': 'ou l’archive .tar.gz',
      'dl.macos.other': 'ou pour les Mac Intel',
      'dl.macos': 'Une archive pour les Mac avec puce Apple, macOS 14 et plus récent.',
      'dl.android': 'Une application pour téléphones : rejoignez une séance lancée depuis un ordinateur ou animez la vôtre, jusqu’à trois personnes.',
      'dl.soon': 'Bientôt disponible',
      'dl.online': 'Vous préférez ne rien installer ? <a href="watch/" data-umami-event="try-online" data-umami-event-place="downloads">Essayez Golether en ligne</a>, directement dans le navigateur.',
      'dl.button': 'Télécharger',
      'dl.version': 'version ',
      'dl.for': 'Télécharger pour ',
      'foot.license': 'Golether est un logiciel libre sous licence GPL-3.0-or-later.',
      'foot.source': 'Code source',
      'foot.guide': 'Guide d’utilisation',
      'foot.privacy': 'Confidentialité',
      'foot.releases': 'Toutes les versions'
    },
    pt: {
      'page.title': 'Golether: partilhem momentos juntos',
      'nav.language': 'Idioma',
      'nav.join': 'Como entrar',
      'nav.host': 'Como convidar',
      'nav.download': 'Transferir',
      'nav.try': 'Experimentar online',
      'hero.title': 'Partilhem momentos <em>juntos</em><br>estejam onde estiverem',
      'hero.lead': '<span>Filmes e fotografias: a mesma imagem para todos, mesmo a milhares de quilómetros.</span> <span>Câmaras, voz, reações e desenho sobre a imagem, tudo numa só janela.</span>',
      'hero.download': 'Transferir o Golether',
      'hero.how': 'Tenho um convite',
      'hero.free': 'Gratuito e de código aberto · Windows, Linux, macOS e o navegador · Android em breve',
      'hero.shot': 'A janela do Golether: um filme no leitor, os participantes e a conversa à direita',
      'hero.sync': 'em sincronia',
      'uses.eyebrow': 'Para quem',
      'uses.title': 'Não só para noites de cinema',
      'uses.sub': 'Sempre que várias pessoas precisam de ver a mesma coisa no mesmo momento.',
      'uses.1.title': 'Amigos e família',
      'uses.1.text': 'Um filme à noite ou as fotografias de uma viagem, com quem vive noutra cidade.',
      'uses.2.title': 'Aprendizagem',
      'uses.2.text': 'Um explicador analisa um filme com o aluno: cada um escolhe as suas legendas e a sua faixa de áudio, e um momento importante pode ser assinalado na própria imagem.',
      'uses.3.title': 'Análise de gravações',
      'uses.3.text': 'Um treinador vê com a equipa a gravação de um jogo: a pausa e o avanço funcionam para todos ao mesmo tempo, e uma jogada pode ser desenhada sobre a imagem.',
      'uses.4.title': 'Trabalho com fotografias',
      'uses.4.text': 'Um fotógrafo mostra uma sessão a um cliente: folheiam-na juntos, ou cada um amplia um pormenor por sua conta. O local e a câmara não vão nas cópias.',
      'join.eyebrow': 'Foi convidado',
      'join.title': 'Quatro passos até à sessão',
      'join.sub': 'Só precisa do Golether no computador e da ligação que um amigo lhe enviou.',
      'join.1.title': 'Clique na ligação',
      'join.1.text': 'O navegador pergunta se quer abrir o Golether. Diga que sim.',
      'join.2.title': 'Confirme',
      'join.2.text': 'O Golether mostra o nome da sessão e quem o convida. Prima “Entrar”.',
      'join.3.title': 'Compare o código',
      'join.3.text': 'Você e o anfitrião veem as mesmas três palavras e um número. Digam-nos um ao outro em voz alta.',
      'join.4.title': 'Divirta-se',
      'join.4.text': 'O anfitrião deixa-o entrar e o filme ou as fotografias aparecem para todos no mesmo momento.',
      'join.note': '<b>A ligação não abriu a aplicação?</b> Peça antes a <b>chave</b> ao anfitrião: copie-a, abra o Golether, cole-a no campo “Convite” e prima “Ligar”. Uma ligação é válida durante 15 minutos e uma chave até uma hora; todo o grupo pode entrar com um só convite.',
      'code.1': 'bordo',
      'code.2': 'rio',
      'code.3': 'pedra',
      'host.eyebrow': 'Você convida',
      'host.title': 'Mostre um filme ou fotografias',
      'host.sub': 'O filme e as fotografias ficam no seu computador e vão diretamente para os seus convidados.<br>Até cinco pessoas veem juntas.',
      'host.1.title': 'Escolha o que mostrar',
      'host.1.text': 'Prima “Criar uma sessão” e escolha um filme, uma fotografia ou uma pasta inteira. Nada é enviado para nenhum servidor.',
      'host.2.title': 'Envie o convite',
      'host.2.text': 'Prima “Convidar”, copie a ligação e publique-a na conversa do grupo. Todos entram com a mesma ligação.',
      'host.3.title': 'Deixe-os entrar',
      'host.3.text': 'Compare o código com o seu convidado de viva voz, deixe-o entrar e carregue em reproduzir.',
      'why.eyebrow': 'Porquê o Golether',
      'why.title': 'Como no mesmo sofá',
      'why.sub': 'Tudo para uma noite em conjunto cabe numa janela.',
      'why.1.title': 'Perfeitamente sincronizado',
      'why.1.text': 'Reproduzir, pausar e recuar funcionam para todos ao mesmo tempo, seja quem for que carregue no botão.',
      'why.2.title': 'Qualidade original',
      'why.2.text': 'O filme não é recodificado numa transmissão desfocada: todos veem o ficheiro tal como é.',
      'why.3.title': 'Caras e vozes',
      'why.3.text': 'A câmara e a voz de todos ao lado da imagem, com cancelamento de eco e um volume para cada pessoa.',
      'why.4.title': 'Privado',
      'why.4.text': 'A ligação é cifrada e vai diretamente entre vocês. Ninguém entra sem a aprovação do anfitrião, e por predefinição o local e a câmara de uma fotografia ficam consigo.',
      'why.5.title': 'As suas legendas e o seu áudio',
      'why.5.text': 'Cada um escolhe as legendas e a faixa de áudio por si, independentemente dos outros.',
      'why.6.title': 'Gratuito para sempre',
      'why.6.text': 'Código aberto. Sem contas, sem anúncios, sem subscrições.',
      'why.7.title': 'Filmes e fotografias',
      'why.7.text': 'Mostre um filme ou uma pasta inteira de fotografias. Folheiem o álbum juntos, ou abra uma fotografia só para si e amplie um pormenor.',
      'why.8.title': 'Desenho sobre a imagem',
      'why.8.text': 'Assinale um pormenor diretamente na imagem de um filme ou numa fotografia: todos veem a linha, e cada pessoa desenha na sua própria cor.',
      'why.9.title': 'Reações e conversa',
      'why.9.text': 'Os emojis flutuam sobre a imagem com o nome de quem os enviou, e a conversa está mesmo ao lado.',
      'dl.eyebrow': 'Transferir',
      'dl.title': 'Obtenha o Golether',
      'dl.sub': 'A mesma versão para todos os que veem juntos.<br>As atualizações são propostas dentro da aplicação.',
      'dl.windows': 'Um instalador para Windows 10 e mais recente. As ligações de convite abrem a aplicação sozinhas.',
      'dl.linux': 'Um pacote para Ubuntu, Debian e derivados: o sistema instala com ele os componentes de vídeo e câmaras.',
      'dl.windows.other': 'ou o arquivo portátil .zip',
      'dl.linux.other': 'ou o arquivo .tar.gz',
      'dl.macos.other': 'ou para Mac com Intel',
      'dl.macos': 'Um arquivo para Mac com Apple silicon, macOS 14 e mais recente.',
      'dl.android': 'Uma aplicação para telemóveis: junte-se a uma sessão de um computador ou organize a sua, até três pessoas.',
      'dl.soon': 'Em breve',
      'dl.online': 'Prefere não instalar nada? <a href="watch/" data-umami-event="try-online" data-umami-event-place="downloads">Experimente o Golether online</a>, diretamente no navegador.',
      'dl.button': 'Transferir',
      'dl.version': 'versão ',
      'dl.for': 'Transferir para ',
      'foot.license': 'O Golether é software livre sob a licença GPL-3.0-or-later.',
      'foot.source': 'Código-fonte',
      'foot.guide': 'Guia do utilizador',
      'foot.privacy': 'Privacidade',
      'foot.releases': 'Todas as versões'
    },
    ru: {
      'page.title': 'Golether: делитесь моментами вместе',
      'nav.language': 'Язык',
      'nav.join': 'Как войти',
      'nav.host': 'Как пригласить',
      'nav.download': 'Скачать',
      'nav.try': 'Попробовать онлайн',
      'hero.title': 'Делитесь моментами <em>вместе</em><br>где бы вы ни были',
      'hero.lead': '<span>Кино и фотографии: один кадр у всех в один момент, даже за тысячи километров.</span> <span>Камеры, голос, реакции и рисование поверх кадра — в одном окне.</span>',
      'hero.download': 'Скачать Golether',
      'hero.how': 'У меня есть приглашение',
      'hero.free': 'Бесплатно, с открытым кодом · Windows, Linux, macOS и браузер · скоро Android',
      'hero.shot': 'Окно Golether: фильм в плеере, участники и чат справа',
      'hero.sync': 'синхронно',
      'uses.eyebrow': 'Кому пригодится',
      'uses.title': 'Не только для вечера кино',
      'uses.sub': 'Когда нескольким людям нужно видеть одно и то же в один момент.',
      'uses.1.title': 'Друзья и семья',
      'uses.1.text': 'Кино по вечерам или фото из поездки с теми, кто живёт в другом городе.',
      'uses.2.title': 'Обучение',
      'uses.2.text': 'Репетитор разбирает фильм с учеником: у каждого свои субтитры и звуковая дорожка, а важный момент можно обвести прямо на кадре.',
      'uses.3.title': 'Разбор записей',
      'uses.3.text': 'Тренер с командой смотрит запись игры: пауза и перемотка срабатывают у всех сразу, а схему розыгрыша можно нарисовать поверх кадра.',
      'uses.4.title': 'Работа с фото',
      'uses.4.text': 'Фотограф показывает клиенту съёмку: листайте вместе или приближайте детали каждый у себя. Место съёмки и данные камеры в копии не уходят.',
      'join.eyebrow': 'Вас пригласили',
      'join.title': 'Четыре шага до сеанса',
      'join.sub': 'Нужен только Golether на компьютере и ссылка, которую прислал друг.',
      'join.1.title': 'Нажмите на ссылку',
      'join.1.text': 'Браузер спросит, открыть ли Golether. Соглашайтесь.',
      'join.2.title': 'Подтвердите вход',
      'join.2.text': 'Golether покажет название сеанса и того, кто вас зовёт. Нажмите «Войти».',
      'join.3.title': 'Сверьте код',
      'join.3.text': 'У вас и у ведущего на экране одни и те же три слова и число. Назовите их друг другу вслух.',
      'join.4.title': 'Наслаждайтесь',
      'join.4.text': 'Ведущий впускает вас, и фильм или фотографии появляются у всех в один момент.',
      'join.note': '<b>Ссылка не открыла приложение?</b> Попросите у ведущего <b>ключ</b>: скопируйте его, откройте Golether, вставьте в поле «Приглашение» и нажмите «Подключиться». Ссылка действует 15 минут, ключ — до часа; по одному приглашению может войти вся компания.',
      'code.1': 'клён',
      'code.2': 'река',
      'code.3': 'камень',
      'host.eyebrow': 'Вы приглашаете',
      'host.title': 'Покажите фильм или фотографии',
      'host.sub': 'Фильм и фотографии остаются на вашем компьютере и идут к гостям напрямую.<br>Вместе смотрят до пяти человек.',
      'host.1.title': 'Выберите, что показать',
      'host.1.text': 'Нажмите «Создать сеанс» и выберите фильм, фотографию или целую папку. На серверы ничего не загружается.',
      'host.2.title': 'Отправьте приглашение',
      'host.2.text': 'Нажмите «Пригласить», скопируйте ссылку и отправьте её в общий чат. По одной ссылке войдут все.',
      'host.3.title': 'Впустите гостей',
      'host.3.text': 'Сверьте код с гостем голосом, впустите его и нажмите «Пуск».',
      'why.eyebrow': 'Почему Golether',
      'why.title': 'Как на одном диване',
      'why.sub': 'Всё для вечера вместе помещается в одном окне.',
      'why.1.title': 'Кадр в кадр',
      'why.1.text': 'Пуск, пауза и перемотка работают сразу у всех, кто бы ни нажал кнопку.',
      'why.2.title': 'Исходное качество',
      'why.2.text': 'Фильм не пережимается в мутную трансляцию: все смотрят файл таким, какой он есть.',
      'why.3.title': 'Лица и голоса',
      'why.3.text': 'Камера и голос каждого рядом с кадром, с подавлением эха и своей громкостью для каждого участника.',
      'why.4.title': 'Только свои',
      'why.4.text': 'Соединение зашифровано и идёт напрямую между вами. Без одобрения ведущего никто не войдёт, а место съёмки и камера на фото по умолчанию остаются у вас.',
      'why.5.title': 'Свои субтитры и озвучка',
      'why.5.text': 'Субтитры и звуковую дорожку каждый выбирает сам, независимо от остальных.',
      'why.6.title': 'Бесплатно навсегда',
      'why.6.text': 'Открытый исходный код. Без регистрации, рекламы и подписок.',
      'why.7.title': 'И кино, и фото',
      'why.7.text': 'Покажите фильм или целую папку с фотографиями. Листайте альбом вместе или откройте фото только у себя и приблизьте деталь.',
      'why.8.title': 'Рисование поверх кадра',
      'why.8.text': 'Обведите деталь прямо на кадре фильма или на фото: линию видят все, и у каждого свой цвет.',
      'why.9.title': 'Реакции и чат',
      'why.9.text': 'Смайлы проплывают поверх кадра с именем отправителя, а чат — тут же, рядом.',
      'dl.eyebrow': 'Скачать',
      'dl.title': 'Установите Golether',
      'dl.sub': 'У всех, кто смотрит вместе, должна быть одна версия.<br>Об обновлениях приложение сообщит само.',
      'dl.windows': 'Установщик для Windows 10 и новее. Ссылки-приглашения открывают приложение сами.',
      'dl.linux': 'Пакет для Ubuntu, Debian и родственных систем: вместе с ним система поставит компоненты для видео и камер.',
      'dl.windows.other': 'или переносной архив .zip',
      'dl.linux.other': 'или архив .tar.gz',
      'dl.macos.other': 'или для Mac на Intel',
      'dl.macos': 'Архив для Mac на Apple silicon, macOS 14 и новее.',
      'dl.android': 'Приложение для телефона: подключайтесь к сеансу с компьютера или проведите свой — до трёх человек.',
      'dl.soon': 'Скоро',
      'dl.online': 'Не хотите ничего устанавливать? <a href="watch/" data-umami-event="try-online" data-umami-event-place="downloads">Попробуйте Golether онлайн</a>, прямо в браузере.',
      'dl.button': 'Скачать',
      'dl.version': 'версия ',
      'dl.for': 'Скачать для ',
      'foot.license': 'Golether — свободная программа под лицензией GPL-3.0-or-later.',
      'foot.source': 'Исходный код',
      'foot.guide': 'Руководство',
      'foot.privacy': 'Конфиденциальность',
      'foot.releases': 'Все выпуски'
    }
  };

  // What the HTML says is the English table.
  var english = { 'page.title': document.title, 'nav.language': 'Language', 'dl.version': 'version ', 'dl.for': 'Download for ' };
  var nodes = document.querySelectorAll('[data-t]');
  nodes.forEach(function (node) { english[node.getAttribute('data-t')] = node.innerHTML; });
  var alts = document.querySelectorAll('[data-t-alt]');
  alts.forEach(function (node) { english[node.getAttribute('data-t-alt')] = node.getAttribute('alt'); });
  texts.en = english;

  var language = 'en';
  var release = null;
  var names = { windows: 'Windows', linux: 'Linux', macos: 'macOS' };

  function text(key) {
    return (texts[language] && texts[language][key]) || english[key] || '';
  }

  function detectSystem() {
    var agent = navigator.userAgent || '';
    if (/Android|iPhone|iPad|iPod/i.test(agent)) { return null; }
    if (/Windows/i.test(agent)) { return 'windows'; }
    if (/Macintosh|Mac OS X/i.test(agent)) { return 'macos'; }
    if (/Linux|X11/i.test(agent)) { return 'linux'; }
    return null;
  }

  // Only https addresses of a release are put on a button; anything else in the file is ignored.
  function safeUrl(value) {
    return typeof value === 'string' && /^https:\/\/[^\s"'<>]+$/.test(value) ? value : null;
  }

  function showDownloads() {
    var mine = detectSystem();
    document.querySelectorAll('.download').forEach(function (card) {
      var system = card.getAttribute('data-os');
      var entry = release && release[system];
      var version = entry && typeof entry.version === 'string' && /^[0-9][0-9A-Za-z.\-]{0,30}$/.test(entry.version) ? entry.version : null;
      card.querySelector('.meta').textContent = version ? text('dl.version') + version : '';
      // Each link names the field of updates.json it takes: url (the archive or the installer) or deb.
      card.querySelectorAll('a[data-field]').forEach(function (link) {
        var url = entry && safeUrl(entry[link.getAttribute('data-field')]);
        if (url) { link.setAttribute('href', url); }
      });
      card.classList.toggle('mine', system === mine);
    });

    var hero = document.getElementById('hero-download');
    var entry = mine && release && release[mine];
    if (mine) {
      hero.querySelector('span').textContent = text('dl.for') + names[mine];
      // On Linux the package of the system comes first, as on the card.
      var deb = entry && safeUrl(entry.deb);
      var url = deb || (entry && safeUrl(entry.url));
      hero.setAttribute('href', url || '#download');
      // A click is counted as a download (Umami) only when the button leads to a package, not to the list below.
      if (url) {
        hero.setAttribute('data-umami-event', 'download');
        hero.setAttribute('data-umami-event-package', mine === 'linux' ? (deb ? 'linux-deb' : 'linux-archive') : mine);
        hero.setAttribute('data-umami-event-place', 'hero');
      }
    }
  }

  var picker = document.querySelector('.lang');
  var showPicker = picker && languages ? languages.mountPicker(picker, switchTo) : function () { };

  function apply(next) {
    language = texts[next] ? next : 'en';
    document.documentElement.lang = language;
    document.title = text('page.title');
    nodes.forEach(function (node) { node.innerHTML = text(node.getAttribute('data-t')); });
    alts.forEach(function (node) { node.setAttribute('alt', text(node.getAttribute('data-t-alt'))); });
    if (picker) { picker.setAttribute('aria-label', text('nav.language')); }
    if (languages) {
      document.getElementById('guide-link').setAttribute('href', languages.docUrl(language, 'UserGuide.md'));
      document.getElementById('privacy-link').setAttribute('href', languages.docUrl(language, 'Privacy.md'));
    }
    showPicker(language);
    showDownloads();
  }

  // A chosen language fades the page out and back in, so the switch reads as one change rather than a flicker.
  function switchTo(next) {
    var body = document.body;
    if (window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
      apply(next);
      return;
    }
    body.classList.add('switching');
    window.setTimeout(function () {
      apply(next);
      body.classList.remove('switching');
    }, 160);
  }

  apply(languages ? languages.detect() : 'en');

  // The same file the application reads to learn about new versions.
  fetch('updates.json', { cache: 'no-cache' })
    .then(function (response) { return response.ok ? response.json() : null; })
    .then(function (json) { if (json && typeof json === 'object') { release = json; showDownloads(); } })
    .catch(function () { /* the buttons keep leading to the releases page */ });

  // The steps appear as the reader gets to them.
  var steps = document.querySelectorAll('.steps li');
  if ('IntersectionObserver' in window) {
    var observer = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (entry.isIntersecting) { entry.target.classList.add('seen'); observer.unobserve(entry.target); }
      });
    }, { threshold: 0.2 });
    steps.forEach(function (step, index) {
      step.style.transitionDelay = (index % 4) * 90 + 'ms';
      observer.observe(step);
    });
  } else {
    steps.forEach(function (step) { step.classList.add('seen'); });
  }
})();
