"""Write AuguryCatalogue.cs, the plugin's static Augury data, from an export of the Augury tables.

    python tools/generate_catalogue.py <augury tables export .json> [AuguryCatalogue.cs]

The export is written by tools/export_augury_tables.mjs from the game's patch files (since 3.30.0c, 2026-11-18), or
was written by a development build of the plugin from memory (AuguryTableExport, not in the published plugin); both
decode the tables by poe-tool-dev/dat-schema into the same shape. Run this again after a patch changes the tables.
The output is plain C#, ASCII only, every line within 120 columns, so it builds the same inside the HUD as outside it.
"""

import json
import re
import sys
from datetime import datetime

WIDTH = 120


def literal(text):
    """A C# string literal, ASCII only: anything else as a \\u escape."""
    if text is None:
        return 'null'

    out = []

    for ch in text:
        if ch == '\\':
            out.append('\\\\')
        elif ch == '"':
            out.append('\\"')
        elif ch == '\n':
            out.append('\\n')
        elif ch == '\r':
            continue
        elif 0x20 <= ord(ch) < 0x7F:
            out.append(ch)
        else:
            out.append('\\u%04X' % ord(ch))

    return '"' + ''.join(out) + '"'


def wrapped(text, indent):
    """A string literal split into concatenated pieces so that no line passes WIDTH."""
    whole = literal(text)

    if text is None or len(indent) + len(whole) + 2 <= WIDTH:
        return whole

    room = WIDTH - len(indent) - 8
    pieces, current = [], ''

    # Split on spaces inside the escaped text, never inside an escape.
    for word in re.findall(r'\S+\s*|\s+', literal(text)[1:-1]):
        if current and len(current) + len(word) > room:
            pieces.append(current)
            current = ''

        current += word

    pieces.append(current)

    return ('\n' + indent + '+ ').join('"%s"' % p for p in pieces)


def strings(values, indent):
    if not values:
        return '[]'

    one_line = '[' + ', '.join(literal(v) for v in values) + ']'

    if len(indent) + len(one_line) + 2 <= WIDTH:
        return one_line

    inner = indent + '    '
    return '[\n' + ''.join(inner + wrapped(v, inner) + ',\n' for v in values) + indent + ']'


def mod_of(reference):
    return (reference or {}).get('mod') or {}


def family_of(reward_id):
    """The reward without the hard-mode prefix or the level-band digit: WarstaffTransformSpeed1 and 2 share one."""
    return re.sub(r'\d+$', '', without_hard_mode(reward_id))


def without_hard_mode(reward_id):
    return reward_id[len('HardMode'):] if reward_id.startswith('HardMode') else reward_id


#: Families kept split whatever the rule finds, each with why. A family's Id keys saved weights and Must take marks,
#: so a split, once shipped, stays.
KEEP_SPLIT = {
    # Split by the rule in 3.30.0c (both tier 3). 3.30.0.4 made the single Divine Orb need two copies (tier 2) and
    # left "additional Divine Orbs" at three, so the same-tier test no longer finds them.
    'CurrencyOpulentDivineOrb',
}


def families_of(rewards):
    """
    Each reward's family by Id: family_of, except where two rows of one family (outside the HardMode set) read
    differently at overlapping area levels and the same tier, or the family is in KEEP_SPLIT. Those are two rewards,
    not one reward's level bands, and each keeps its digit: CurrencyOpulentDivineOrb1 "an additional Divine Orb" and 2
    "additional Divine Orbs", both levels 68-100 (3.30.0c), the only such pair then. Elsewhere differing texts are one
    reward's level bands ("additional" at low levels, "many additional" above), one family, so no family that existed
    before changes. The tier test is needed: without it RarityHigh1 and 2 (overlapping levels, different tiers) would
    split (checked on the 3.30.0.3 and 3.30.0.4 tables, 2026-11-19).
    """
    by_family = {}

    for reward in rewards:
        by_family.setdefault(family_of(reward['Id']), []).append(reward)

    split = set(KEEP_SPLIT)

    for family, rows in by_family.items():
        normal = [r for r in rows if r['Unknown6'] != 3]

        for i, a in enumerate(normal):
            for b in normal[i + 1:]:
                if (a['Description'] != b['Description'] and a['Tier'] == b['Tier']
                        and a['MinLevel'] <= b['MaxLevel'] and b['MinLevel'] <= a['MaxLevel']):
                    split.add(family)

    return {r['Id']: without_hard_mode(r['Id']) if family_of(r['Id']) in split else family_of(r['Id'])
            for r in rewards}


def main():
    source = sys.argv[1]
    target = sys.argv[2] if len(sys.argv) > 2 else 'AuguryCatalogue.cs'
    export = json.load(open(source, encoding='utf-8'))
    tables = export['tables']

    factions = tables['AuguryFactions']['rows']
    fates = tables['AuguryCards']['rows']
    tiers = tables['AuguryCardTiers']['rows']
    rewards = tables['AuguryRewardOutcomes']['rows']

    faction_ids = [f['Id'] for f in factions]
    tiers_by_fate = {}

    for tier in tiers:
        tiers_by_fate.setdefault(tier['Card']['row'], []).append(tier)

    lines = []
    w = lines.append

    w('// Generated by tools/generate_catalogue.py on %s. Do not edit by hand: regenerate.'
      % datetime.now().strftime('%Y-%m-%d %H:%M'))
    w('// From %s (exported %s),'
      % (source.replace('\\', '/').split('/')[-1], export.get('exported', '?')))
    w('// decoded by %s.' % export.get('schema', 'dat-schema'))
    w('')
    w('namespace AutoAugury;')
    w('')
    w('/// <summary>')
    w('/// The Augury league\'s static data, read off the client\'s tables: the three factions, the 25 Fates with')
    w('/// their faction and three tiers (ability text and the monster mod each gives the boss), and the reward')
    w('/// outcomes an offer can roll. Generated by tools/generate_catalogue.py; regenerate after a patch.')
    w('///')
    w('/// Read, not inferred: every Fate has one faction, and every offer seen came from one faction')
    w('/// (2026-11-14). Observed, not read: a reward\'s tier is the copies it needs; the panel marks tier 2 rewards')
    w('/// "To claim this reward, invoke two copies of it" and a tier 3 reward (CurrencyOpulent1) "three copies".')
    w('/// HardMode rewards are a separate set (the table\'s mode value 3 against 2 for the rest, matching the Id')
    w('/// prefix in all rows), 205 of the 360. None has been seen offered; they are taken to be Ruthless\'s')
    w('/// (suspected, not read), and the plugin weights them by their family\'s weight like the rest.')
    w('/// </summary>')
    w('internal static class AuguryCatalogue')
    w('{')
    w('')
    w('    /// <summary>When the client\'s tables were exported: regenerate after a patch that changes them.</summary>')
    w('    public const string ExportedAt = %s;' % literal(export.get('exported', '?')))
    w('')
    w('    /// <summary>The dat-schema the export was decoded with.</summary>')
    w('    public const string Schema = %s;' % literal(export.get('schema', '?')))
    w('')

    w('    public static readonly AuguryFaction[] Factions =')
    w('    [')

    for f in factions:
        w('        new(%s, %s, %s),' % (literal(f['Id']), literal(f['Name']), literal(f['Icon'])))

    w('    ];')
    w('')
    w('    public static readonly AuguryFate[] Fates =')
    w('    [')

    for fate in fates:
        faction = fate['Unknown4']
        faction_id = faction_ids[faction['row']] if faction and faction.get('table') == 'AuguryFactions' else None
        indent = '            '

        head = '        new(%s, %s, %s,' % (literal(fate['Id']), literal(fate['Name']), literal(faction_id))

        if len(head) + len(literal(fate['Art'])) + 2 <= WIDTH:
            w('%s %s,' % (head, literal(fate['Art'])))
        else:
            w(head)
            w('            %s,' % literal(fate['Art']))
        w('        [')

        for tier in sorted(tiers_by_fate.get(fate['_index'], []), key=lambda t: t['Tier']):
            mod = mod_of(tier['Mod'])
            w('            new(%d, %s,' % (tier['Tier'], wrapped(tier['Description'], indent + '    ')))
            w('                %s,' % wrapped(tier['Body'], indent + '    '))
            w('                %s, %s),' % (literal(mod.get('key')), strings(mod.get('stats', []), indent + '    ')))

        w('        ]),')

    w('    ];')
    w('')
    w('    public static readonly AuguryReward[] Rewards =')
    w('    [')

    families = families_of(rewards)

    for reward in rewards:
        mods = [mod_of(e).get('key') for e in reward['Unknown3'].get('elements', []) if mod_of(e).get('key')]
        achievements = [e.get('id') for e in reward['Achievements'].get('elements', []) if e and e.get('id')]
        indent = '            '

        w('        new(%s, %s, %d, %s,' % (literal(reward['Id']), literal(families[reward['Id']]), reward['Tier'],
                                       'true' if reward['Unknown6'] == 3 else 'false'))
        w('            %s,' % wrapped(reward['Description'], indent))
        w('            %d, %d, %s,' % (reward['MinLevel'], reward['MaxLevel'], literal(reward['Icon'])))
        w('            %s, %s),' % (strings(mods, indent), literal(achievements[0] if achievements else None)))

    w('    ];')
    w('}')
    w('')
    w('/// <summary>A faction: its table Id (Savage, Filth, Classic), the name the game shows, and its icon.</summary>')
    w('internal sealed record AuguryFaction(string Id, string Name, string Icon);')
    w('')
    w('/// <summary>')
    w('/// One Fate: its table Id, the name the game shows, its faction\'s Id, its card art, and its three tiers.')
    w('/// </summary>')
    w('internal sealed record AuguryFate(string Id, string Name, string FactionId, string Art, '
      'AuguryFateTier[] Tiers);')
    w('')
    w('/// <summary>')
    w('/// One tier of a Fate: the card\'s short text, the full text in the game\'s markup, and the monster mod the')
    w('/// boss gets, with its stats and values ("stat_id min - max").')
    w('/// </summary>')
    w('internal sealed record AuguryFateTier(int Tier, string Description, string Body, string ModKey, '
      'string[] ModStats);')
    w('')
    w('/// <summary>')
    w('/// One reward outcome: its table Id; its family (the Id without the HardMode prefix or level-band digit, so')
    w('/// one family is one reward across area levels; the digit kept where two rows read differently at the same')
    w('/// levels and tier, as the two Divine Orb rewards do); its tier; whether it is of the HardMode set; the text')
    w('/// the panel shows; the area levels it can roll at; its icon; the monster mods that make its drops; and the')
    w('/// challenge it counts towards.')
    w('/// </summary>')
    w('internal sealed record AuguryReward(string Id, string Family, int Tier, bool HardMode, string Description,')
    w('    int MinLevel, int MaxLevel, string Icon, string[] DropMods, string Achievement);')

    text = '\n'.join(lines) + '\n'
    long_lines = [i + 1 for i, line in enumerate(text.split('\n')) if len(line) > WIDTH]

    if long_lines:
        print('lines over %d columns: %s' % (WIDTH, long_lines[:20]))

    open(target, 'w', encoding='ascii', newline='\n').write(text)
    print('wrote %s: %d factions, %d fates, %d tiers, %d rewards, %d reward families'
          % (target, len(factions), len(fates), len(tiers), len(rewards), len(set(families_of(rewards).values()))))


if __name__ == '__main__':
    main()
