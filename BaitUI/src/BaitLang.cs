using System;
using System.Collections.Generic;

// Translations (U55) for the window labels: tabs, buttons, headings and on/off words. The game's language picks the
// column; anything without a translation (and every server message) stays in English.
public static class L
{
    // English|German|Spanish|French|Portuguese (Brazil)
    const string Table = @"
Thralls|Diener|Siervos|Serviteurs|Servos
Groups|Gruppen|Grupos|Groupes|Grupos
Display|Anzeige|Pantalla|Affichage|Exibição
Keys|Tasten|Teclas|Touches|Teclas
Tools|Werkzeuge|Herramientas|Outils|Ferramentas
Admin|Admin|Admin|Admin|Admin
Close|Schließen|Cerrar|Fermer|Fechar
Roster|Liste|Lista|Liste|Lista
Call|Rufen|Llamar|Appeler|Chamar
Stow|Wegschicken|Guardar|Ranger|Guardar
Stay|Bleib|Quieto|Reste|Fica
Follow|Folgen|Seguir|Suivre|Seguir
Tether|Anbinden|Atar|Attacher|Prender
Untether|Losbinden|Soltar|Détacher|Soltar
Heal|Heilen|Curar|Soigner|Curar
Revive|Wiederbeleben|Revivir|Ranimer|Reviver
Track|Verfolgen|Rastrear|Suivre|Rastrear
Untrack|Nicht verfolgen|No rastrear|Ne plus suivre|Parar
Page|Seite|Página|Page|Página
Show|Zeige|Mostrar|Voir|Mostrar
All|Alle|Todos|Tous|Todos
Zombies|Zombies|Zombis|Zombies|Zumbis
Animals|Tiere|Animales|Animaux|Animais
Sort|Sortieren|Ordenar|Trier|Ordenar
Number|Nummer|Número|Numéro|Número
Level|Stufe|Nivel|Niveau|Nível
HP|LP|PV|PV|PV
per page|pro Seite|por página|par page|por página
Bring all here|Alle herholen|Traer a todos|Tous ici|Trazer todos
Whole roster|Ganze Liste|Toda la lista|Toute la liste|Lista inteira
Teams|Teams|Equipos|Équipes|Equipes
Night, PvP and home|Nacht, PvP und Zuhause|Noche, PvP y hogar|Nuit, JcJ et maison|Noite, PvP e casa
Stow all|Alle wegschicken|Guardar todos|Tout ranger|Guardar todos
Stow active|Aktive wegschicken|Guardar activos|Ranger les actifs|Guardar ativos
Stow tethered|Angebundene wegschicken|Guardar atados|Ranger les attachés|Guardar presos
Follow all|Alle folgen|Todos siguen|Tous suivent|Todos seguem
Stay all|Alle bleiben|Todos quietos|Tous restent|Todos ficam
Deaths|Tode|Muertes|Morts|Mortes
Team|Team|Equipo|Équipe|Equipe
none yet|noch keins|ninguno aún|aucune|nenhuma ainda
Call team|Team rufen|Llamar equipo|Appeler l'équipe|Chamar equipe
Save active|Aktive speichern|Guardar activos|Sauver les actifs|Salvar ativos
New team|Neues Team|Nuevo equipo|Nouvelle équipe|Nova equipe
All to kennel|Alle in den Zwinger|Todos a la perrera|Tous au chenil|Todos ao canil
Blood moon|Blutmond|Luna de sangre|Lune de sang|Lua de sangue
Name tags|Namensschilder|Etiquetas|Étiquettes|Etiquetas
Party list|Gruppenliste|Lista de grupo|Liste du groupe|Lista do grupo
Fed tags|Futter-Anzeige|Etiquetas de comida|Repas|Etiquetas de comida
Party bars|Gruppenbalken|Barras de grupo|Barres du groupe|Barras do grupo
Kill feed|Abschüsse|Muertes en pantalla|Fil des kills|Abates
Level glow|Stufen-Leuchten|Brillo de nivel|Lueur de niveau|Brilho de nível
Low HP flash|Blinken bei wenig LP|Destello PV bajos|Flash PV bas|Piscar PV baixo
Low HP sound|Ton bei wenig LP|Sonido PV bajos|Son PV bas|Som PV baixo
Downed tags|Niedergestreckt-Schild|Etiqueta de caído|Étiquette à terre|Etiqueta de caído
Map|Karte|Mapa|Carte|Mapa
HP strip|LP-Leiste|Barra de PV|Bande PV|Faixa de PV
Compact|Kompakt|Compacto|Compact|Compacto
Theme|Design|Tema|Thème|Tema
Dark|Dunkel|Oscuro|Sombre|Escuro
Light|Hell|Claro|Clair|Claro
High contrast|Hoher Kontrast|Alto contraste|Contraste élevé|Alto contraste
Colour-blind bars|Farbenblind-Balken|Barras daltónicas|Barres daltoniens|Barras daltônicas
Tame banner|Zähm-Banner|Aviso de doma|Bannière d'apprivoisement|Aviso de doma
Damage numbers|Schadenszahlen|Números de daño|Chiffres de dégâts|Números de dano
Target lines|Ziellinien|Líneas de objetivo|Lignes de cible|Linhas de alvo
Downed panel|Niedergestreckt-Liste|Panel de caídos|Panneau à terre|Painel de caídos
Kills|Abschüsse|Muertes|Kills|Abates
Own kills|Eigene|Propias|Les siens|Próprios
Special|Besondere|Especiales|Spéciaux|Especiais
Setup popup|Einrichtungsfenster|Ventana de ajuste|Fenêtre de réglage|Janela de ajuste
Name color|Namensfarbe|Color del nombre|Couleur du nom|Cor do nome
Bar color|Balkenfarbe|Color de barra|Couleur de barre|Cor da barra
Colors for|Farben für|Colores para|Couleurs pour|Cores para
all thralls|alle Diener|todos los siervos|tous les serviteurs|todos os servos
Tag size|Schildgröße|Tamaño etiqueta|Taille étiquettes|Tamanho etiqueta
Window size|Fenstergröße|Tamaño ventana|Taille fenêtre|Tamanho janela
Re-center|Zentrieren|Centrar|Recentrer|Centralizar
Columns|Spalten|Columnas|Colonnes|Colunas
Mode|Modus|Modo|Mode|Modo
Auto|Auto|Auto|Auto|Auto
Status|Status|Estado|État|Estado
on|an|sí|oui|sim
off|aus|no|non|não
Roster key|Listen-Taste|Tecla de lista|Touche liste|Tecla da lista
Wheel key|Rad-Taste|Tecla de rueda|Touche roue|Tecla da roda
Go-to key|Hingehen-Taste|Tecla de ir|Touche aller|Tecla ir
Thrall keys|Diener-Tasten|Teclas de siervo|Touches serviteur|Teclas de servo
Press a key...|Taste drücken...|Pulsa una tecla...|Appuie sur une touche...|Aperte uma tecla...
Thrall page|Dienerseite|Ficha|Fiche|Ficha
Bestiary|Bestiarium|Bestiario|Bestiaire|Bestiário
Session summary|Sitzungsübersicht|Resumen de sesión|Résumé de session|Resumo da sessão
Top|Bestenliste|Ranking|Classement|Ranking
Tame help|Zähmhilfe|Ayuda de doma|Aide apprivoisement|Ajuda de doma
My stats|Meine Werte|Mis datos|Mes stats|Meus dados
Server stats|Server-Werte|Datos del servidor|Stats du serveur|Dados do servidor
Craft bait|Köder herstellen|Fabricar cebo|Fabriquer appât|Fabricar isca
Command|Befehl|Orden|Commande|Comando
Run|Los|Ir|OK|Ir
Care|Pflege|Cuidado|Soins|Cuidado
Perks|Talente|Ventajas|Atouts|Vantagens
Post and patrol|Posten und Streife|Puesto y patrulla|Poste et patrouille|Posto e patrulha
More|Mehr|Más|Plus|Mais
Rename|Umbenennen|Renombrar|Renommer|Renomear
Auto-revive|Auto-Wiederbeleben|Auto-revivir|Auto-ranimer|Auto-reviver
Auto-heal|Auto-Heilen|Auto-curar|Auto-soin|Auto-curar
Second chance|Zweite Chance|Segunda oportunidad|Seconde chance|Segunda chance
Kennel|Zwinger|Perrera|Chenil|Canil
Tough|Zäh|Duro|Robuste|Resistente
Hit|Schlag|Golpe|Frappe|Golpe
Regen|Regeneration|Regeneración|Régénération|Regeneração
Taunt|Spott|Provocar|Provocation|Provocar
Reset perks|Talente zurücksetzen|Reiniciar ventajas|Réinitialiser|Redefinir
Guard here|Hier bewachen|Vigilar aquí|Garder ici|Vigiar aqui
Stop guarding|Bewachen beenden|Dejar de vigilar|Arrêter la garde|Parar de vigiar
Add patrol point|Streifenpunkt hinzu|Añadir punto|Ajouter un point|Adicionar ponto
Start patrol|Streife starten|Empezar patrulla|Lancer la patrouille|Iniciar patrulha
Stop patrol|Streife beenden|Parar patrulla|Arrêter la patrouille|Parar patrulha
Clear points|Punkte löschen|Borrar puntos|Effacer les points|Limpar pontos
Scout|Erkunden|Explorar|Éclaireur|Explorar
Evolve|Entwickeln|Evolucionar|Évoluer|Evoluir
Scream|Schreien|Gritar|Crier|Gritar
Retire|Ruhestand|Retirar|Retraite|Aposentar
Release|Freilassen|Liberar|Libérer|Libertar
Sure?|Sicher?|¿Seguro?|Sûr ?|Certeza?
Prev|Zurück|Anterior|Préc.|Anterior
Next|Weiter|Siguiente|Suiv.|Próximo
New thrall|Neuer Diener|Nuevo siervo|Nouveau serviteur|Novo servo
Name|Name|Nombre|Nom|Nome
Skip|Überspringen|Omitir|Passer|Pular
Don't ask again|Nicht mehr fragen|No preguntar más|Ne plus demander|Não perguntar mais
Apply|Übernehmen|Aplicar|Appliquer|Aplicar
Settings|Einstellungen|Ajustes|Réglages|Ajustes
Feed rates|Futtermengen|Comidas|Repas|Refeições
Exclude|Ausschließen|Excluir|Exclure|Excluir
Players|Spieler|Jugadores|Joueurs|Jogadores
Casual|Locker|Casual|Détente|Casual
Default|Standard|Por defecto|Par défaut|Padrão
Hardcore|Hardcore|Extremo|Hardcore|Hardcore
Admin log|Admin-Protokoll|Registro admin|Journal admin|Registro admin
Add aimed|Anvisiertes hinzu|Añadir apuntado|Ajouter visé|Adicionar mirado
Clear list|Liste leeren|Vaciar lista|Vider la liste|Limpar lista
Allow|Erlauben|Permitir|Autoriser|Permitir
Open|Öffnen|Abrir|Ouvrir|Abrir
Back|Zurück|Volver|Retour|Voltar
Heal all|Alle heilen|Curar todos|Tout soigner|Curar todos
Revive all|Alle beleben|Revivir todos|Tout ranimer|Reviver todos
Bring all|Alle holen|Traer todos|Tout amener|Trazer todos
Bring|Holen|Traer|Amener|Trazer
Owned|Besitz|Propios|Possédés|Possuídos
Active|Aktiv|Activos|Actifs|Ativos
Tamed only|Nur gezähmte|Solo domados|Apprivoisés|Só domados
Reset|Zurücksetzen|Reiniciar|Réinitialiser|Zerar
Cap|Limit|Límite|Limite|Limite
";

    static Dictionary<string, string[]> map;
    static int lang = -2;

    static int Lang()
    {
        if (lang != -2) return lang;
        string l = "";
        try { l = (Localization.ActiveLanguage ?? "").ToLowerInvariant(); } catch { }
        lang = l.StartsWith("german") ? 0 : (l.StartsWith("spanish") || l.StartsWith("latam")) ? 1 : l.StartsWith("french") ? 2 : (l.StartsWith("brazil") || l.StartsWith("portug")) ? 3 : -1;
        return lang;
    }

    public static void LanguageChanged() { lang = -2; }

    public static string T(string en)
    {
        int i = Lang();
        if (i < 0 || en == null) return en;
        if (map == null)
        {
            map = new Dictionary<string, string[]>();
            foreach (string line in Table.Split('\n'))
            {
                var p = line.Trim().Split('|');
                if (p.Length == 5) map[p[0]] = new[] { p[1], p[2], p[3], p[4] };
            }
        }
        string[] t;
        return map.TryGetValue(en, out t) ? t[i] : en;
    }

    public static string OnOff(bool b) { return T(b ? "on" : "off"); }
}
