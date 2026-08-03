# Forge V4 material integration for the FortnitePorting Blender plugin.
#
# Ported from the "Forge V4 Shader Setup" Blender addon by psdkyo
# (GPL-3.0). The node-graph setup, texture detection, layout, and module
# wiring logic below are a faithful port of that addon's core, adapted to
# run headlessly inside FortnitePorting's import pipeline (no operators,
# panels, or Downloads/Documents scanning - the Forge blend path comes
# from FortnitePorting's settings instead).
#
# Original addon: Forge V4 Shader Setup, author psdkyo, GPL-3.0.

import os
import bpy

DEBUG = False

FORGE_MATERIAL_NAME = "Forge V4"

def _debug(*args):
    if DEBUG:
        print("[FortnitePorting] Forge:", *args)


def _blend_file_has_material(path, material_name):
    """Peek inside a .blend file's material list without actually loading
    anything (an empty `data_to` assignment means nothing gets appended)."""
    try:
        with bpy.data.libraries.load(path, link=True) as (data_from, _data_to):
            return material_name in data_from.materials
    except Exception:
        return False


def ensure_forge_data(blend_path):
    """Make sure the Forge V4 node groups are available in this session.

    Validates that `blend_path` points at a real .blend file containing a
    Material named exactly "Forge V4" (that is the V4 check), then appends
    that material (link=False) - which registers all FV4 node groups - if it
    isn't already present. Returns (True, None) on success or
    (False, "reason") on failure. The explicit path from the FortnitePorting
    settings is the only source - no filesystem scanning."""
    if FORGE_MATERIAL_NAME in bpy.data.materials:
        return True, None

    if not blend_path:
        return False, ("no Forge blend file set. Set the path to "
                       "'Forge V4 Official.blend' in the FortnitePorting Blender settings.")

    path = bpy.path.abspath(blend_path)
    if not os.path.isfile(path):
        return False, f"Forge blend file does not exist: {path}"

    if not _blend_file_has_material(path, FORGE_MATERIAL_NAME):
        return False, (f"'{path}' does not contain a material named "
                       f"'{FORGE_MATERIAL_NAME}' - not a Forge V4 blend file?")

    try:
        with bpy.data.libraries.load(path, link=False) as (data_from, data_to):
            if FORGE_MATERIAL_NAME in data_from.materials:
                data_to.materials.append(FORGE_MATERIAL_NAME)
            else:
                return False, f"material '{FORGE_MATERIAL_NAME}' disappeared from '{path}' while loading."

            # Explicitly append every node group. Newer Forge blends no longer have the base
            # "Forge V4" material transitively reference all the part shaders (Hair, Glasses,
            # etc.), so relying on the material alone leaves those groups missing and hair/faceacc
            # parts fail with "Node group 'FV4: Hair' not found". Skip names we already have so
            # we never create '.001' duplicates.
            for ng in data_from.node_groups:
                if bpy.data.node_groups.get(ng) is None:
                    data_to.node_groups.append(ng)
    except Exception as e:
        return False, f"failed to append '{FORGE_MATERIAL_NAME}' from '{path}': {e}"

    return True, None


def match_part(part: str, name: str) -> bool:
    name = name.lower()
    part = part.lower()

    if part == "eye":
        return "eye" in name
    if part == "hair":
        return "hair" in name
    if part == "glass":
        return "glass" in name
    if part == "head":
        return "head" in name
    if part == "body":
        return "body" in name
    if part == "faceacc":
        return any(x in name for x in ("faceacc", "mask", "hood"))

    return part in name

# --- Dynamic node layout ---------------------------------------------------
# The whole shader graph is relaid out after every change, rather than
# placing each new node one at a time:
#
#   1. Columns (X) come from graph topology, not guesswork. Every node's
#      "depth" is its longest path (in link hops) back from the Material
#      Output. Output is depth 0 and is the fixed origin - it is never
#      moved. Every other node's column is `origin_x - depth * COLUMN_WIDTH`,
#      so the graph only ever grows to the left of Output, never the right.
#   2. Rows (Y) are chosen after X is settled, working outward column by
#      column starting at Output: a node's Y is the average row of whatever
#      socket(s) it feeds in the column that's already been placed (one
#      column closer to Output). That's the closest approximation of
#      "shortest link" we can get, since Blender doesn't expose real
#      per-socket pixel coordinates from Python - node dimensions read as
#      (0, 0) until the UI has drawn a node at least once, so this uses a
#      fixed row-height estimate per socket index instead.
#   3. Collisions within a column are resolved by nudging a node down just
#      enough to clear whatever it would otherwise overlap.
LAYOUT_ROW_HEIGHT = 24
LAYOUT_HEADER_HEIGHT = 40
LAYOUT_MARGIN = 6
LAYOUT_COLUMN_WIDTH = 300
# A collapsed/hidden node renders as a single small pill. This is deliberately
# kept at or below LAYOUT_ROW_HEIGHT: rows on the target node (what desired-Y
# is computed from) are LAYOUT_ROW_HEIGHT apart, so if this were any taller,
# every pair of hidden siblings feeding adjacent rows would "overlap" by
# definition and get pushed down - cascading into a much bigger drift with
# every extra sibling instead of sitting level with the rows they feed.
LAYOUT_HIDDEN_HEIGHT = 20

def _estimate_node_height(node):
    if node.hide:
        return LAYOUT_HIDDEN_HEIGHT
    if node.type == 'TEX_IMAGE':
        return 260
    if node.type == 'GROUP' and node.node_tree:
        return LAYOUT_HEADER_HEIGHT + max(1, len(node.inputs)) * LAYOUT_ROW_HEIGHT
    return 120

def _node_bottom(node):
    return node.location.y - _estimate_node_height(node)

def _place_in_column(node_tree, node, x, preferred_y=None, obstacles=None):
    """Move `node` to column `x`. If `preferred_y` is free of any obstacle in
    that column, it's used as-is. Otherwise `node` is nudged down just enough
    to clear whatever it overlaps.

    `obstacles` must be the set of nodes that have already been positioned this
    pass. Nodes not yet placed still sit at stale, pre-layout coordinates
    (0,0 or leftover import positions), so counting them as obstacles would
    shove `node` down to dodge a collision that won't exist once they move -
    which is exactly what caused the whole graph to drift downward column by
    column. When no set is given we fall back to every node in the tree (used
    by standalone callers that aren't doing an ordered pass)."""
    node.location.x = x
    height = _estimate_node_height(node)

    candidates = obstacles if obstacles is not None else node_tree.nodes
    others = [o for o in candidates if o is not node and abs(o.location.x - x) <= LAYOUT_COLUMN_WIDTH / 2]

    if preferred_y is None:
        preferred_y = 0 if not others else min(_node_bottom(o) for o in others) - LAYOUT_MARGIN

    y = preferred_y
    guard = 0
    changed = True
    while changed and guard <= len(others):
        changed = False
        guard += 1
        top, bottom = y, y - height
        for other in others:
            o_top, o_bottom = other.location.y, _node_bottom(other)
            if top > o_bottom - LAYOUT_MARGIN and bottom < o_top + LAYOUT_MARGIN:
                new_y = o_bottom - LAYOUT_MARGIN
                if new_y < y:
                    y = new_y
                    changed = True

    node.location.y = y
    return node

def _socket_row_y(target_node, socket):
    try:
        index = list(target_node.inputs).index(socket)
    except ValueError:
        index = 0
    return target_node.location.y - LAYOUT_HEADER_HEIGHT - index * LAYOUT_ROW_HEIGHT

def find_output_node(node_tree):
    outputs = [n for n in node_tree.nodes if n.type == "OUTPUT_MATERIAL"]
    if not outputs:
        return None
    return next((n for n in outputs if getattr(n, "is_active_output", False)), outputs[0])

def relayout_material(material):
    node_tree = material.node_tree

    # Start from a clean slate: frames (ours from a previous run, or leftover
    # ones from the original imported material) don't move or resize
    # themselves as their contents get relaid out, so stale/orphaned frame
    # boxes just accumulate visual clutter. Removing a frame node only
    # un-parents its children - it doesn't delete them.
    for node in list(node_tree.nodes):
        if node.type == 'FRAME':
            node_tree.nodes.remove(node)

    output_node = find_output_node(node_tree)
    if output_node is None:
        return

    # Longest-path depth of every node reachable backward (via input links)
    # from Output. Output itself is depth 0.
    depths = {}
    def visit(node, cur_depth):
        if depths.get(node, -1) >= cur_depth:
            return
        depths[node] = cur_depth
        for socket in node.inputs:
            for link in socket.links:
                visit(link.from_node, cur_depth + 1)
    visit(output_node, 0)

    if len(depths) <= 1:
        return

    columns = {}
    for node, d in depths.items():
        columns.setdefault(d, []).append(node)

    origin_x = output_node.location.x
    max_depth = max(columns)
    placed = {output_node}

    for depth in range(1, max_depth + 1):
        nodes_in_col = columns.get(depth)
        if not nodes_in_col:
            continue
        x = origin_x - depth * LAYOUT_COLUMN_WIDTH

        desired = {}
        for node in nodes_in_col:
            # Only align to consumers in the immediately-next column (depth - 1).
            # A node can fan out to a consumer several columns closer to Output
            # (e.g. a "_D" texture feeding both Main AND Atmospheric Ambiance,
            # which sits much closer to Output than Main does) - averaging in
            # that far-away row would drag this node off of the row that
            # actually matters (the one on the node immediately to its right).
            rows = [
                _socket_row_y(link.to_node, link.to_socket)
                for out in node.outputs
                for link in out.links
                if link.to_node in placed and depths.get(link.to_node) == depth - 1
            ]
            if not rows:
                # Fan-out only to farther-away columns (rare) - fall back to
                # any already-placed consumer rather than the output row.
                rows = [
                    _socket_row_y(link.to_node, link.to_socket)
                    for out in node.outputs
                    for link in out.links
                    if link.to_node in placed
                ]
            desired[node] = (sum(rows) / len(rows)) if rows else output_node.location.y

        # Place highest-desired-row first so any forced pushes cascade downward.
        # Only already-placed nodes count as collision obstacles (see
        # _place_in_column) - siblings placed earlier in this same column are
        # in `placed` by the time the next one is positioned.
        for node in sorted(nodes_in_col, key=lambda n: -desired[n]):
            _place_in_column(node_tree, node, x, desired[node], obstacles=placed)
            placed.add(node)

    # Nodes that are wired into the graph but NOT reachable from Output - e.g.
    # a branch whose link into the shader got replaced by a later one (adding a
    # module the shader already auto-created leaves the original dangling). They
    # aren't in any depth column and aren't fully unlinked either, so park them
    # in a fresh column left of everything instead of leaving them at a stale
    # spot on top of a live node.
    leftover = [
        n for n in node_tree.nodes
        if n.type != 'FRAME' and n not in placed
        and (any(s.is_linked for s in n.inputs) or any(s.is_linked for s in n.outputs))
    ]
    if leftover:
        x = origin_x - (max_depth + 1) * LAYOUT_COLUMN_WIDTH
        for node in sorted(leftover, key=lambda n: -n.location.y):
            _place_in_column(node_tree, node, x, obstacles=placed)
            placed.add(node)

# Node group names of the Forge shader modules used by the automation. The
# node groups are appended from "Forge V4 Official.blend" (via the "Forge V4"
# material) so these must match the group names in that file exactly.
# Some node groups were renamed between Forge V4 blend releases (e.g. the glasses
# shader is 'FV4: Glass' in the original blend and 'FP4: Glasses' in the newer one).
# Resolve a group by its known name first, then any alias, so both blends work.
_GROUP_ALIASES = {
    'FV4: Glass': ['FV4: Glass', 'FP4: Glasses', 'FV4: Glasses'],
}

def _resolve_group(name):
    group = bpy.data.node_groups.get(name)
    if group is not None:
        return group
    for alias in _GROUP_ALIASES.get(name, []):
        group = bpy.data.node_groups.get(alias)
        if group is not None:
            return group
    # Newer Forge blends sometimes ship a group as a '.001' duplicate (the base datablock was
    # deleted, e.g. 'FV4: Hair' -> 'FV4: Hair.001'). Fall back to the numbered variants.
    for suffix in ('.001', '.002', '.003'):
        group = bpy.data.node_groups.get(name + suffix)
        if group is not None:
            return group
    return None

FORGE_SHADER_GROUPS = {
    "Main": "FV4: Main",
    "Eyes": "FV4 Eyes",
    "Glasses": "FV4: Glass",
    "Hair": "FV4: Hair",
}

def _material_has_forge_shader(material):
    """True if `material` already has one of the character shader groups
    wired in. Guards against silently stacking a second character shader (and
    duplicate Atmospheric Ambiance/Metal Reflection/etc. groups) on top of an
    already-set-up material - the material cache can hand the same datablock
    back for multiple slots."""
    if not material or not material.use_nodes or not material.node_tree:
        return False
    names = set(FORGE_SHADER_GROUPS.values())
    # Also match the resolved datablocks so a shader applied under a '.001'/aliased name
    # (newer Forge blends, e.g. 'FV4: Hair' -> 'FV4: Hair.001') still counts as forged.
    resolved = {id(_resolve_group(n)) for n in names if _resolve_group(n) is not None}
    for node in material.node_tree.nodes:
        if node.type == "GROUP" and node.node_tree and (
                node.node_tree.name in names or id(node.node_tree) in resolved):
            return True
    return False

# Groups with the Shader-in/Shader-out wrapper pattern (checked once against
# each group's actual sockets) - these get spliced onto whatever currently
# drives the Material Output rather than linked into a specific character
# shader's inputs.
WRAPPER_GROUP_NAMES = {
    "FV4: Eyelashes",
    "FV4: Crystal Shader",
    "FV4: Hologram",
    "FV4: Slurp Master Shader",
}

def is_wrapper_group(group_name):
    return group_name in WRAPPER_GROUP_NAMES

def find_group_node(node_tree, group_name):
    # Match the exact name AND the resolved group datablock, so a group applied under a
    # '.001'/aliased name (newer Forge blends) is still recognised as already-present.
    resolved = _resolve_group(group_name)
    for node in node_tree.nodes:
        if node.type == "GROUP" and node.node_tree and (
                node.node_tree.name == group_name or (resolved is not None and node.node_tree == resolved)):
            return node
    return None

def _find_character_shader(node_tree):
    """Returns (part_key, group_name, node) for whichever FORGE_SHADER_GROUPS
    character shader is actually wired into this material's node tree, or
    (None, None, None) if none has been applied yet."""
    for key, group_name in FORGE_SHADER_GROUPS.items():
        node = find_group_node(node_tree, group_name)
        if node is not None:
            return key, group_name, node
    return None, None, None

def set_new_specular_inputs(node, value):
    for socket in node.inputs:
        if socket.name.strip().startswith("NEW Specular"):
            try:
                socket.default_value = value
            except Exception as e:
                print(f"[FortnitePorting] Forge: could not set '{socket.name}' on {node.name}: {e}")

def get_new_specular_value(node):
    for socket in node.inputs:
        if socket.name.strip().startswith("NEW Specular"):
            try:
                return bool(socket.default_value)
            except Exception:
                return None
    return None

# Fortnite Porting labels each texture node by channel, regardless of which
# specific asset/body-part it belongs to - filenames can collide across body
# parts within the same material's node tree (e.g. a stray
# "..._Head_D.png" node ending up alongside the real "..._Body_D.png" one),
# but the label never lies about which channel a texture is.
#
# Fortnite Porting's own DefaultMappings.textures (processing/material/
# mappings.py) recognizes several raw Unreal parameter names per channel, any
# of which can end up as the literal label depending on what the source
# material actually used. Only the aliases plausible on an actual
# character/skin material are included here (e.g. "D"/"Base Color" for
# Diffuse) - environment/prop-only aliases from that same table (like
# "CliffTexture", "Trunk_BaseColor", "PM_Diffuse") are deliberately left out,
# since they'd never appear on a character material this integration targets.
TEXTURE_LABEL_TO_SUFFIX = {
    "diffuse": "_D",
    "d": "_D",
    "base color": "_D",
    "basecolor": "_D",
    "mask": "_M",
    "m": "_M",
    "m mask": "_M",
    "srm": "_S",
    "specularmasks": "_S",
    "s": "_S",
    "specular mask": "_S",
    "specularmask": "_S",
    "normals": "_N",
    "n": "_N",
    "normal": "_N",
    "normalmap": "_N",
    "emissive": "_E",
    "emission": "_E",
    "emissivecolor": "_E",
    "emissivetexture": "_E",
}

def _label_suffix(node, valid_suffixes):
    """Like _detect_texture_suffix, but label-only - no filename fallback.
    Used to tell an authoritative label match apart from a heuristic filename
    guess when disambiguating between multiple same-suffix candidates."""
    label = (node.label or "").strip().lower()
    suffix = TEXTURE_LABEL_TO_SUFFIX.get(label)
    if suffix is not None and suffix in valid_suffixes:
        return suffix
    return None

def _detect_texture_suffix(node, valid_suffixes):
    """Return whichever suffix key in `valid_suffixes` this texture node
    corresponds to. The node's label is checked first (see
    TEXTURE_LABEL_TO_SUFFIX above); only if that doesn't resolve to one of
    the suffixes we're actually looking for does this fall back to matching
    the image filename's suffix."""
    suffix = _label_suffix(node, valid_suffixes)
    if suffix is not None:
        return suffix
    if node.image:
        image_name = node.image.name.upper()
        for candidate in valid_suffixes:
            if image_name.endswith(candidate) or f"{candidate}." in image_name:
                return candidate
    return None

# The channel label alone (TEXTURE_LABEL_TO_SUFFIX) can't disambiguate between
# two textures that both legitimately carry it - Fortnite Porting labels every
# texture by channel only, regardless of body part, so a stray "..._Head_D"
# texture sitting in a Body material's tree can be correctly labelled
# "Diffuse" too, same as the real Body one. Reuses match_part's own keywords
# so "which part is this?" is answered the same way everywhere.
PART_KEYWORDS = ("body", "head", "hair", "faceacc", "eye", "glass")

def _best_texture_for_suffix(material, candidates, suffix=None):
    """When more than one texture node resolves to the same suffix, prefer
    whichever candidate actually carries the channel LABEL for it - a label is
    an explicit, authoritative signal (see TEXTURE_LABEL_TO_SUFFIX), whereas a
    candidate that only matched via the image filename's suffix is a much
    weaker heuristic guess and shouldn't win over a genuinely labelled one.
    Only applies when `suffix` is given (the setup_forge_shader call site
    always has it - callers that don't care about this distinction can omit
    it and fall straight through to the part-keyword tie-break below).

    If that still doesn't narrow it to exactly one (e.g. two candidates both
    carry the label, as when Fortnite Porting genuinely labels a stray texture
    from a different body part the same way), fall back to whichever
    candidate's image name matches the same body-part keyword as the material
    itself, and finally to the first candidate (stable, but not necessarily
    correct) if that still doesn't narrow it down either."""
    if len(candidates) <= 1:
        return candidates[0] if candidates else None

    if suffix is not None:
        labelled = [n for n in candidates if _label_suffix(n, {suffix}) == suffix]
        if len(labelled) == 1:
            return labelled[0]
        if labelled:
            candidates = labelled

    for part in PART_KEYWORDS:
        if match_part(part, material.name):
            part_matches = [n for n in candidates if n.image and match_part(part, n.image.name)]
            if len(part_matches) == 1:
                return part_matches[0]
    return candidates[0]

def setup_forge_shader(material, shader_name, links):
    node_tree = material.node_tree

    def add_group(name, location, width=256, hidden=False):
        group = _resolve_group(name)
        if not group:
            raise ValueError(f"Node group '{name}' not found.")
        node = node_tree.nodes.new(type='ShaderNodeGroup')
        node.node_tree = group
        node.location = location
        node.width = width
        node.hide = hidden
        return node

    def getNodeByLabel(identifier):
        for node in node_tree.nodes:
            if node.label == identifier or node.name == identifier:
                return node
        return None

    new_specular = 0
    skin_tint = None
    eyelash_mask_tex = None

    for node in node_tree.nodes:
        if node.type == "GROUP" and node.node_tree and "SwizzleRoughnessToGreen" in node.inputs:
            new_specular = node.inputs["SwizzleRoughnessToGreen"].default_value
        if node.label == "Skin Boost Color And Exponent":
            skin_tint = getNodeByLabel("Skin Boost Color And Exponent")
        if node.label == "SkinTint":
            skin_tint = getNodeByLabel("SkinTint")
        if node.type == "TEX_IMAGE" and node.label and node.label.strip().lower() == "eyelashmask":
            eyelash_mask_tex = node

    useful_groups = ["FPv3 Pre FX", "FPv3 Composite"]

    # The Material Output is never deleted - it's reused as-is below (and
    # kept as the fixed origin the whole graph is laid out from), instead of
    # being torn down and recreated at a hardcoded position every time.
    for node in list(node_tree.nodes):
        if node.type != 'TEX_IMAGE' and node.location.x < 300:
            if node.type == "GROUP":
                if node.node_tree.name not in useful_groups:
                    node_tree.nodes.remove(node)
            elif node.type == "NodeClosureInput" or node.type == "NodeClosureOutput":
                    node_tree.nodes.remove(node)

    ambiance = None

    forge_shader = add_group(shader_name, (350, 0))
    # Atmospheric Ambiance is a Main-shader-only enhancement - gated on which
    # shader is actually being set up here, not on the material's name (a
    # material named e.g. "Head_Hair" could match a "head"/"body" name guess
    # while still legitimately being set up with the Hair shader).
    if shader_name == FORGE_SHADER_GROUPS["Main"]: ambiance = add_group('FV4: Atmospheric Ambiance', (650, 0))

    if "Skin Tint" in forge_shader.inputs and skin_tint is not None:
        node_tree.links.new(skin_tint.outputs[0], forge_shader.inputs["Skin Tint"])
        skin_tint.hide = True
    set_new_specular_inputs(forge_shader, True if new_specular == 1 else False)

    reflection = None
    if not (match_part("glass", material.name) or match_part("eye", material.name)):
        has_metal_reflect = "Metal Reflect Color" in forge_shader.inputs
        has_fuzz_mask = "Fuzz Mask" in forge_shader.inputs

        if has_metal_reflect:
            reflection = add_group('FV4: Metal Environment Reflection Color', (15, -595))
            node_tree.links.new(reflection.outputs['Metal Reflect Color'], forge_shader.inputs['Metal Reflect Color'])

        if has_fuzz_mask:
            # No fuzz mask texture ships with the shader library or gets
            # auto-detected from the source material - left with no image
            # assigned for the user to fill in manually.
            fuzz_tex = node_tree.nodes.new(type='ShaderNodeTexImage')
            fuzz_tex.hide = True
            node_tree.links.new(fuzz_tex.outputs['Color'], forge_shader.inputs['Fuzz Mask'])

            fuzz_map = add_group('FV4: Fuzz Mapping', (-300, -1150), hidden=True)
            node_tree.links.new(fuzz_map.outputs['Vector'], fuzz_tex.inputs['Vector'])

    output = find_output_node(node_tree)
    if output is None:
        output = node_tree.nodes.new(type='ShaderNodeOutputMaterial')
        output.location = (0, 0)

    forge_shader_output = forge_shader.outputs[0]
    if ambiance != None:
        node_tree.links.new(forge_shader_output, ambiance.inputs['Shader'])
        node_tree.links.new(ambiance.outputs['Shader'], output.inputs['Surface'])
    else:
        node_tree.links.new(forge_shader_output, output.inputs['Surface'])

    links_map = links

    values_nodes_types = ["TEX_IMAGE", "GROUP", "VALUE", "RGB"]
    forge_nodes_names = ["fv4: atmospheric ambiance", "fv4: main", "fv4 eyes", "fv4: glass", "fv4: hair", "fv4: fuzz mapping"]

    # Collect every candidate texture per suffix first rather than linking as
    # soon as one is found - two textures can both legitimately resolve to the
    # same suffix (see _best_texture_for_suffix above), and linking on every
    # match would just have the last one found in node_tree.nodes silently
    # overwrite the previous link with no guarantee the right one wins.
    texture_candidates = {}

    for node in node_tree.nodes:
        if node.location.x > 300:
                if node.type in values_nodes_types:
                    if node.type == "GROUP":
                        if node.node_tree.name.strip().lower() not in forge_nodes_names:
                            node.location = (node.location.x + 800, node.location.y)
                    else: node.location = (node.location.x + 800, node.location.y)
        else:
            if node.type == 'TEX_IMAGE' and node.image:
                suffix = _detect_texture_suffix(node, links_map)
                if suffix is not None:
                    texture_candidates.setdefault(suffix, []).append(node)

    for suffix, candidates in texture_candidates.items():
        node = _best_texture_for_suffix(material, candidates, suffix)
        input_name = links_map[suffix]
        try:
            # Connect color output to all of their corresponding inputs
            for socket in forge_shader.inputs:
                if socket.name == input_name:
                    if suffix == "_N" and not (match_part("glass", material.name)) and reflection is not None: node_tree.links.new(node.outputs['Color'], reflection.inputs[input_name])
                    if (suffix == "_M" and ambiance != None): node_tree.links.new(node.outputs['Color'], ambiance.inputs[input_name])
                    if (suffix == "_D" and ambiance != None): node_tree.links.new(node.outputs['Color'], ambiance.inputs["Color Selector"])
                    node_tree.links.new(node.outputs['Color'], socket)
                    _debug(f"Connected {node.image.name} to {input_name}")
        except Exception as e:
            _debug(f"Error with '{input_name}': {e}")
        # Every candidate for this suffix is still a channel texture the user
        # doesn't need visible, even the one(s) that didn't win - collapse them
        # all.
        for candidate in candidates:
            candidate.hide = True

    if shader_name == FORGE_SHADER_GROUPS["Eyes"]:
        try:
            esm = add_group('FV4: Eye Specular Mapping', (15, -300))
        except ValueError as e:
            esm = None
            print(f"[FortnitePorting] Forge: could not add Eye Specular Mapping: {e}")

        if esm is not None:
            esm_rename = {"Preset Sepcular Highlight": "Specular Highlight Mask"}
            for esm_output in esm.outputs:
                out_name = esm_output.name.strip()
                if out_name == "Vector":
                    continue
                target_name = esm_rename.get(esm_output.name, esm_output.name)
                if target_name in forge_shader.inputs:
                    node_tree.links.new(esm_output, forge_shader.inputs[target_name])

        if eyelash_mask_tex is not None:
            try:
                eyelashes = add_shader_wrapper(material, "FV4: Eyelashes")
                if "Use Lashes" in eyelashes.inputs:
                    eyelashes.inputs["Use Lashes"].default_value = True
                if "Eyelashes Mask" in eyelashes.inputs:
                    node_tree.links.new(eyelash_mask_tex.outputs['Color'], eyelashes.inputs["Eyelashes Mask"])
                    eyelash_mask_tex.hide = True
            except Exception as e:
                print(f"[FortnitePorting] Forge: could not add Eyelashes module: {e}")

    # Materials from an "instance" variant (Fortnite Porting names these with
    # "_inst" - a second, separate material slot layered on top of a base
    # skin, e.g. for FX overlays) get the Slurp Master Shader wrapper added
    # automatically, regardless of which character shader they used.
    if "_inst" in material.name.lower():
        try:
            add_shader_wrapper(material, "FV4: Slurp Master Shader")
        except Exception as e:
            print(f"[FortnitePorting] Forge: could not auto-add Slurp Master Shader: {e}")

    relayout_material(material)

def _find_socket(sockets, *names):
    for s in sockets:
        if s.name in names:
            return s
    return None

# Suffix -> candidate input names to try (in order) on whatever module is
# being wired up. Most groups just call these "D"/"M"/"N"/"S", but some (e.g.
# "FV4: Slurp Master Shader") use a more descriptive name for one of them -
# "Base Normal" instead of plain "N" - so each suffix tries a few aliases
# rather than a single fixed name.
TEXTURE_SUFFIX_ALIASES = {
    "_D": ("D",),
    "_M": ("M",),
    "_N": ("N", "Base Normal", "Normal"),
    "_S": ("S",),
}

# Which channel on a "prefer_from" character shader (Main/Hair/...) each
# suffix corresponds to - those groups always use the plain single-letter name.
_SUFFIX_TO_CHANNEL = {"_D": "D", "_M": "M", "_N": "N", "_S": "S"}

def _hookup_texture_suffixes(node_tree, module, suffix_aliases=TEXTURE_SUFFIX_ALIASES, prefer_from=None):
    """Auto-link D/M/N/S-style inputs on `module` to whatever is the current
    best source for each channel, collapsing a texture once it's wired in.

    If `prefer_from` is given (the character shader this module sits
    alongside, e.g. "FV4: Main"), each channel first tries whatever is
    CURRENTLY feeding that shader's own D/M/N/S input. That's always the last
    thing in any texture-modifying chain already applied - another Extra
    Module, or the base texture if nothing else has touched that channel yet
    - so a module added after a chain of other modules picks up the latest
    result instead of the raw, unmodified texture. Only falls back to a raw
    suffix-matched texture search for channels that source didn't cover.

    Used both for the generic Extra Modules and for shader wrappers (Slurp,
    Crystal Shader...) that also take D/M/N/S-style texture inputs alongside
    their Shader in/out."""
    connected = set()

    if prefer_from is not None:
        for suffix, channel in _SUFFIX_TO_CHANNEL.items():
            if suffix not in suffix_aliases:
                continue
            source_socket = prefer_from.inputs.get(channel)
            if source_socket is None or not source_socket.is_linked:
                continue
            dst = _find_socket(module.inputs, *suffix_aliases[suffix])
            if dst is None:
                continue
            node_tree.links.new(source_socket.links[0].from_socket, dst)
            connected.add(suffix)

    for node in node_tree.nodes:
        if node.type == "TEX_IMAGE" and node.image:
            suffix = _detect_texture_suffix(node, suffix_aliases)
            if suffix is not None and suffix not in connected:
                dst = _find_socket(module.inputs, *suffix_aliases[suffix])
                if dst is not None:
                    node_tree.links.new(node.outputs['Color'], dst)
                    connected.add(suffix)
                    node.hide = True

# Fortnite Porting labels every texture node with the raw Unreal material
# parameter name (unconditionally, in FPv4 - see processing/material/
# mappings.py DefaultMappings.textures), and this specific channel has 8
# recognized aliases all mapping to the same "FX Mask" shader slot: "FX Mask",
# "FX", "SkinFX_Mask", "SkinFX Mask", "TechArtMask", "FxMask", "FX_Mask",
# "Input FX". Any of them can show up as the literal label depending on which
# name the source Unreal material actually used. "skinfx" (bare, no
# "_Mask"/" Mask" suffix) isn't one of FPv4's own aliases but is kept too
# since it's what was originally reported seen in practice.
SLURP_MASK_TEXTURE_LABELS = {
    "fx mask", "fx", "skinfx_mask", "skinfx mask", "techartmask",
    "fxmask", "fx_mask", "input fx", "skinfx",
}

def _hookup_slurp_mask(node_tree, module):
    """"FV4: Slurp Master Shader"'s "Slurp Mask" input (a plain float, not a
    D/M/N/S-style channel so _hookup_texture_suffixes doesn't cover it) comes
    from the blue channel of a texture labelled with one of
    SLURP_MASK_TEXTURE_LABELS, if one exists in the material - split via a
    Separate Color node rather than linking the whole (RGB) color output into
    a float socket."""
    slurp_mask = module.inputs.get("Slurp Mask")
    if slurp_mask is None:
        return
    skinfx_tex = next(
        (n for n in node_tree.nodes if n.type == "TEX_IMAGE" and n.label
         and n.label.strip().lower() in SLURP_MASK_TEXTURE_LABELS),
        None,
    )
    if skinfx_tex is None:
        return
    separate = node_tree.nodes.new(type='ShaderNodeSeparateColor')
    separate.label = "Slurp Mask (Blue)"
    separate.hide = True
    node_tree.links.new(skinfx_tex.outputs['Color'], separate.inputs['Color'])
    node_tree.links.new(separate.outputs['Blue'], slurp_mask)
    skinfx_tex.hide = True

def _find_any_forge_shader_node(node_tree):
    for group_name in FORGE_SHADER_GROUPS.values():
        node = find_group_node(node_tree, group_name)
        if node is not None:
            return node
    return None

def add_module_node(material, shader_name, module_name, location=None, output_rename=None):
    node_tree = material.node_tree
    output_rename = output_rename or {}

    def get_group(name):
        if not material.use_nodes: return None

        for node in node_tree.nodes:
            if node.type == "GROUP" and node.node_tree and node.node_tree.name == name: return node
        raise ValueError(f"Node Group '{name}' not found.")

    def add_group(name, location, width=256, hidden=False):
        group = _resolve_group(name)
        if not group:
            raise ValueError(f"Node group '{name}' not found.")
        node = node_tree.nodes.new(type='ShaderNodeGroup')
        node.node_tree = group
        node.location = location
        node.width = width
        node.hide = hidden
        return node

    forge_main = get_group(shader_name)
    module = add_group(module_name, location or (0, 0))

    # Fill the module's own D/M/N/S-style inputs BEFORE relinking forge_main's
    # matching outputs below - otherwise forge_main.inputs['D'] would already
    # point at this module's own output by the time we read "what currently
    # feeds D", creating a self-loop (module.D output -> module.D input).
    _hookup_texture_suffixes(node_tree, module, prefer_from=forge_main)

    for output in module.outputs:
        output_name = output.name
        target_name = output_rename.get(output_name, output_name)
        if target_name in forge_main.inputs:
            try:
                node_tree.links.new(module.outputs[output_name], forge_main.inputs[target_name])
            except KeyError:
                _debug(f"Error while linking '{output_name}' into {forge_main.name} Node Group.")
        else:
            _debug(f"{forge_main.name} Node Group has no input named '{target_name}'")

    relayout_material(material)
    # Some modules (Crystal Dual Color, Emmision FX Textures, Z-Mask...) have
    # no socket names that match anything automatically, so they end up with
    # no links at all. relayout_material only positions nodes reachable from
    # the Material Output, so park anything left fully disconnected instead
    # of letting it default to the same spot as every other orphaned module.
    align_unlinked_nodes(node_tree)

def add_shader_wrapper(material, group_name):
    node_tree = material.node_tree
    output_node = find_output_node(node_tree)
    if output_node is None:
        raise ValueError("No Material Output node found, apply a Forge shader to this material first.")

    group = bpy.data.node_groups.get(group_name)
    if not group:
        raise ValueError(f"Node group '{group_name}' not found.")
    module = node_tree.nodes.new(type='ShaderNodeGroup')
    module.node_tree = group
    module.width = 256

    module_shader_in = next((s for s in module.inputs if s.type == 'SHADER'), None)
    module_shader_out = next((s for s in module.outputs if s.type == 'SHADER'), None)
    if module_shader_in is None or module_shader_out is None:
        raise ValueError(f"Node group '{group_name}' has no shader input/output to splice in.")

    surface = output_node.inputs.get('Surface')
    if surface is not None and surface.is_linked:
        node_tree.links.new(surface.links[0].from_socket, module_shader_in)

    node_tree.links.new(module_shader_out, surface)

    # Wrapper groups aren't just a Shader passthrough - e.g. "FV4: Slurp
    # Master Shader" also takes its own "M" and "Base Normal" texture inputs
    # alongside the shader it wraps, so fill those in from whatever's already
    # in the tree the same way Extra Modules do (preferring whatever the
    # material's own character shader currently uses for that channel).
    _hookup_texture_suffixes(node_tree, module, prefer_from=_find_any_forge_shader_node(node_tree))

    if group_name == "FV4: Slurp Master Shader":
        _hookup_slurp_mask(node_tree, module)

    relayout_material(material)
    return module

vertical_spacing = 40
right_margin = 0

UNLINKED_FRAME_NAME = "Forge_Unlinked_Frame"

def align_unlinked_nodes(node_tree):

    # 1. Find nodes with no links at all.
    unlinked_nodes = []

    for node in node_tree.nodes:
        if node.type == 'FRAME':
            continue

        has_links = False

        for input in node.inputs:
            if input.is_linked:
                has_links = True
                break

        if not has_links:
            for output in node.outputs:
                if output.is_linked:
                    has_links = True
                    break

        if not has_links:
            unlinked_nodes.append(node)

    if not unlinked_nodes:
        _debug("No unlinked nodes found.")
        return

    # 2. Sort alphabetically (label if set, else name).
    unlinked_nodes.sort(key=lambda n: (n.label if n.label else n.name).lower())

    # 3. Find the rightmost position already in use.
    positioned = [n for n in node_tree.nodes if n.type != 'FRAME']
    max_x = max((n.location.x for n in positioned), default=0)
    start_x = max_x + right_margin
    start_y = max((n.location.y for n in positioned), default=0)

    # 4. Group them into a dedicated frame, out of the shader's way.
    frame = node_tree.nodes.get(UNLINKED_FRAME_NAME)
    if frame is None:
        frame = node_tree.nodes.new(type='NodeFrame')
        frame.name = UNLINKED_FRAME_NAME
    frame.label = "Unlinked Nodes"

    for i, node in enumerate(unlinked_nodes):
        node.location.x = start_x
        node.location.y = start_y - (i * vertical_spacing)
        node.hide = True
        node.parent = frame

    _debug(f"{len(unlinked_nodes)} nodes aligned.")

# --- FortnitePorting integration entry points ------------------------------

# Per-part character shader group + texture-suffix links, exactly as the
# reference addon's Apply operators dispatch them.
_PART_SHADER_SETUPS = {
    "eye": ("Eyes", {"_D": "Diffuse", "_S": "Specular", "_E": "Emision Color"}),
    "faceacc": ("Hair", {"_D": "D", "_S": "S", "_N": "N", "_M": "M", "_STRANDSN": "N Strands", "_STRANDS": "Strands", "_TANGENT": "Tangent"}),
    "hair": ("Hair", {"_D": "D", "_S": "S", "_N": "N", "_M": "M", "_STRANDSN": "N Strands", "_STRANDS": "Strands", "_TANGENT": "Tangent"}),
    "glass": ("Glasses", {"_D": "D", "_N": "N"}),
}
_MAIN_SHADER_SETUP = ("Main", {"_D": "D", "_M": "M", "_N": "N", "_S": "S", "_E": "Emissive Color"})


def apply_forge(material, part_hint=None):
    """Apply the appropriate Forge V4 character shader to `material`.

    `part_hint` is an optional lowercase part string ("eye", "hair", "glass",
    "head", "body", "faceacc") derived from the mesh's EFortCustomPartType.
    The material NAME wins over the hint - a head mesh contains eye/hair
    materials - so specific part keywords in the name are checked first, then
    the hint, then a head/body name match; anything unresolved falls back to
    the Main shader. Skips materials that already carry a Forge character
    shader (the material cache can hand back already-forged datablocks).

    Returns True if the shader was applied, False if skipped."""
    if material is None:
        return False

    material.use_nodes = True
    if material.node_tree is None:
        return False

    if _material_has_forge_shader(material):
        return False

    part = None
    # Same dispatch order as the reference Apply operators.
    for candidate in ("eye", "faceacc", "hair", "glass"):
        if match_part(candidate, material.name):
            part = candidate
            break

    if part is None and part_hint:
        part = part_hint.lower()

    if part is None:
        for candidate in ("head", "body"):
            if match_part(candidate, material.name):
                part = candidate
                break

    part_key, links = _PART_SHADER_SETUPS.get(part, _MAIN_SHADER_SETUP)
    setup_forge_shader(material, FORGE_SHADER_GROUPS[part_key], links)
    align_unlinked_nodes(material.node_tree)
    return True


def _target_mesh_objects():
    """Mesh objects an extras action should act on: every selected mesh
    object, or the active object if it's a mesh but nothing is selected."""
    meshes = [o for o in bpy.context.selected_objects if o.type == 'MESH']
    if meshes:
        return meshes
    active = bpy.context.active_object
    if active and active.type == 'MESH':
        return [active]
    return []


# Per-module eligibility, matching the reference addon's
# _eligible_materials_for_module / _eligible_materials_for_wrapper rules:
# groups filed under its "Eye Shader" category only ever go on Eyes
# materials, "FV4: Hyper Detail" is Main-shader-only (body/head skin detail
# work), and every other standalone module links into Main or Hair shaders
# only - never Eyes/Glass, whose inputs a generic module's D/N outputs could
# otherwise corrupt.
EYE_ONLY_GROUPS = {
    "FV4: Eye Specular Mapping",
    "FV4: Eyelashes",
}
MAIN_ONLY_MODULES = {
    "FV4: Hyper Detail",
}

def _module_eligible(material, node_group_name, part_key):
    """Reference eligibility rules, given which character shader (`part_key`,
    a FORGE_SHADER_GROUPS key or None) is wired into `material`."""
    if node_group_name in EYE_ONLY_GROUPS:
        return part_key == "Eyes" and match_part("eye", material.name)
    if is_wrapper_group(node_group_name):
        # Non-eye wrappers splice onto any set-up material.
        return True
    if node_group_name in MAIN_ONLY_MODULES:
        return part_key == "Main" and (match_part("body", material.name) or match_part("head", material.name))
    return part_key in ("Main", "Hair")


def add_extra(node_group_name, blend_path):
    """Add a Forge V4 extra module/wrapper to the selected mesh objects'
    materials. `node_group_name` is the exact node-group name sent by the
    FortnitePorting app. Returns (ok, message)."""
    ok, reason = ensure_forge_data(blend_path)
    if not ok:
        return False, reason

    objects = _target_mesh_objects()
    if not objects:
        return False, "no mesh object selected - select the imported meshes first."

    output_rename = {"Preset Sepcular Highlight": "Specular Highlight Mask"} if node_group_name == "FV4: Eye Specular Mapping" else None

    applied = []
    errors = []
    seen = set()

    for obj in objects:
        for material in obj.data.materials:
            if not material:
                continue
            if material.name in seen:
                continue
            seen.add(material.name)

            material.use_nodes = True
            if material.node_tree is None:
                continue

            try:
                part_key, shader_name, _shader_node = _find_character_shader(material.node_tree)
                if is_wrapper_group(node_group_name):
                    if find_output_node(material.node_tree) is None:
                        continue
                    if not _module_eligible(material, node_group_name, part_key):
                        continue
                    add_shader_wrapper(material, node_group_name)
                else:
                    if shader_name is None:
                        # Standalone modules link into a character shader's
                        # inputs - skip materials that haven't been forged.
                        continue
                    if not _module_eligible(material, node_group_name, part_key):
                        continue
                    add_module_node(material, shader_name, node_group_name, output_rename=output_rename)
                applied.append(material.name)
            except Exception as e:
                errors.append(f"{material.name}: {e}")

    if applied:
        message = f"added '{node_group_name}' to: {', '.join(applied)}"
        if errors:
            message += f" (errors: {'; '.join(errors)})"
        return True, message

    if errors:
        return False, f"failed to add '{node_group_name}': {'; '.join(errors)}"
    return False, (f"no eligible material found for '{node_group_name}' - apply "
                   f"Forge materials to the selection first.")
