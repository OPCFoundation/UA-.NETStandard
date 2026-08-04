#!/usr/bin/env python3
"""
Generate the OpenUSD text assets for the GeneratorServer sample.

This script writes generator.usda and Powerhouse.usda in the same directory as the
script. Those outputs are generated artifacts and must never be hand-edited;
rerun `python generate_generator_assets.py` from this Assets directory instead.
"""

from __future__ import annotations

import math
from pathlib import Path
from typing import Iterable, Sequence


INDENT = "    "


def f(value: float) -> str:
    """Format numbers deterministically for readable USDA diffs."""
    if abs(value) < 0.0005:
        value = 0.0
    return f"{value:.3f}"


def q(value: str) -> str:
    return f'"{value}"'


def line(level: int, text: str = "") -> str:
    return f"{INDENT * level}{text}"


def vec(values: Sequence[float]) -> str:
    return "(" + ", ".join(f(value) for value in values) + ")"


def matrix4d_translate(translate: Sequence[float]) -> str:
    x, y, z = translate
    return (
        f"( (1.000, 0.000, 0.000, 0.000), (0.000, 1.000, 0.000, 0.000), "
        f"(0.000, 0.000, 1.000, 0.000), ({f(x)}, {f(y)}, {f(z)}, 1.000) )"
    )


def attr_lines(attributes: Iterable[str], level: int) -> list[str]:
    return [line(level, attribute) for attribute in attributes]


def prim(
    type_name: str,
    name: str,
    attributes: Iterable[str] = (),
    children: Iterable[str] = (),
    metadata: str | None = None,
    level: int = 0,
) -> str:
    suffix = f" ({metadata})" if metadata else ""
    lines = [line(level, f'def {type_name} {q(name)}{suffix}'), line(level, "{")]
    lines.extend(attr_lines(attributes, level + 1))
    child_text = list(children)
    if child_text:
        if lines[-1] != line(level, "{"):
            lines.append("")
        lines.extend(child_text)
    lines.append(line(level, "}"))
    return "\n".join(lines)


def material(
    name: str,
    color: Sequence[float],
    roughness: float = 0.55,
    metallic: float = 0.0,
    level: int = 1,
) -> str:
    shader = prim(
        "Shader",
        "PreviewSurface",
        [
            'uniform token info:id = "UsdPreviewSurface"',
            f"color3f inputs:diffuseColor = {vec(color)}",
            f"float inputs:roughness = {f(roughness)}",
            f"float inputs:metallic = {f(metallic)}",
            'token outputs:surface',
        ],
        level=level + 1,
    )
    return prim(
        "Material",
        name,
        ['token outputs:surface.connect = </Generator/Looks/' + name + '/PreviewSurface.outputs:surface>'],
        [shader],
        level=level,
    )


def bind(material_name: str) -> str:
    return f"rel material:binding = </Generator/Looks/{material_name}>"


def xform_attrs(
    translate: Sequence[float] | None = None,
    scale: Sequence[float] | None = None,
    rotate_xyz: Sequence[float] | None = None,
    rotate_x: float | None = None,
    rotate_y: float | None = None,
    rotate_z: float | None = None,
    transform: Sequence[float] | None = None,
) -> list[str]:
    attributes: list[str] = []
    order: list[str] = []
    if translate is not None:
        attributes.append(f"double3 xformOp:translate = {vec(translate)}")
        order.append("xformOp:translate")
    if rotate_xyz is not None:
        attributes.append(f"double3 xformOp:rotateXYZ = {vec(rotate_xyz)}")
        order.append("xformOp:rotateXYZ")
    if rotate_x is not None:
        attributes.append(f"double xformOp:rotateX = {f(rotate_x)}")
        order.append("xformOp:rotateX")
    if rotate_y is not None:
        attributes.append(f"double xformOp:rotateY = {f(rotate_y)}")
        order.append("xformOp:rotateY")
    if rotate_z is not None:
        attributes.append(f"double xformOp:rotateZ = {f(rotate_z)}")
        order.append("xformOp:rotateZ")
    if scale is not None:
        attributes.append(f"double3 xformOp:scale = {vec(scale)}")
        order.append("xformOp:scale")
    if transform is not None:
        attributes.append(f"matrix4d xformOp:transform = {matrix4d_translate(transform)}")
        order.append("xformOp:transform")
    if order:
        quoted_order = ", ".join(q(item) for item in order)
        attributes.append(f"uniform token[] xformOpOrder = [{quoted_order}]")
    return attributes


def cube(
    name: str,
    size: Sequence[float],
    translate: Sequence[float],
    material_name: str,
    level: int,
    display_color: Sequence[float] | None = None,
) -> str:
    attributes = ["double size = 1"]
    attributes.extend(xform_attrs(translate=translate, scale=size))
    attributes.append(bind(material_name))
    if display_color is not None:
        # A prim a live binding recolours must declare primvars:displayColor
        # itself. Writing the attribute from the override layer alone puts the
        # colour in the file but leaves the renderer with nothing to update, so
        # the value never animates in a viewport.
        attributes.append(f"color3f[] primvars:displayColor = [({f(display_color[0])}, {f(display_color[1])}, {f(display_color[2])})]")
    return prim("Cube", name, attributes, level=level)


def cylinder(
    name: str,
    axis: str,
    radius: float,
    height: float,
    translate: Sequence[float],
    material_name: str,
    level: int,
    display_color: Sequence[float] | None = None,
) -> str:
    attributes = [f'uniform token axis = "{axis}"', f"double radius = {f(radius)}", f"double height = {f(height)}"]
    attributes.extend(xform_attrs(translate=translate))
    attributes.append(bind(material_name))
    if display_color is not None:
        attributes.append(f"color3f[] primvars:displayColor = [({f(display_color[0])}, {f(display_color[1])}, {f(display_color[2])})]")
    return prim("Cylinder", name, attributes, level=level)


def sphere(
    name: str,
    radius: float,
    translate: Sequence[float],
    material_name: str,
    level: int,
    visible: bool = True,
) -> str:
    attributes = [f"double radius = {f(radius)}"]
    attributes.extend(xform_attrs(translate=translate))
    attributes.append(bind(material_name))
    if not visible:
        attributes.append('token visibility = "invisible"')
    return prim("Sphere", name, attributes, level=level)



def blade(index: int, angle: float) -> str:
    attributes = ["double size = 1"]
    attributes.extend(xform_attrs(translate=(0.000, 0.260, 0.000), rotate_z=angle, scale=(0.070, 0.440, 0.025)))
    attributes.append(bind("MetalGrey"))
    return prim("Cube", f"Blade_{index}", attributes, level=3)


def annulus_mesh(name: str, inner_radius: float, outer_radius: float, z: float, segments: int, material_name: str) -> str:
    points: list[tuple[float, float, float]] = []
    indices: list[int] = []
    for segment in range(segments):
        angle0 = 2.0 * math.pi * segment / segments
        angle1 = 2.0 * math.pi * (segment + 1) / segments
        base = len(points)
        points.extend(
            [
                (outer_radius * math.cos(angle0), outer_radius * math.sin(angle0), z),
                (outer_radius * math.cos(angle1), outer_radius * math.sin(angle1), z),
                (inner_radius * math.cos(angle1), inner_radius * math.sin(angle1), z),
                (inner_radius * math.cos(angle0), inner_radius * math.sin(angle0), z),
            ]
        )
        indices.extend([base, base + 1, base + 2, base + 3])

    point_text = ",\n".join(line(2, vec(point)) for point in points)
    index_rows = []
    for start in range(0, len(indices), 16):
        index_rows.append(line(2, ", ".join(str(value) for value in indices[start : start + 16])))
    attributes = [
        "point3f[] points = [\n" + point_text + "\n" + line(1, "]"),
        "int[] faceVertexCounts = [" + ", ".join("4" for _ in range(segments)) + "]",
        "int[] faceVertexIndices = [\n" + ",\n".join(index_rows) + "\n" + line(1, "]"),
        bind(material_name),
        'token visibility = "invisible"',
    ]
    return prim("Mesh", name, attributes, level=1)


def floor_mesh() -> str:
    attributes = [
        "point3f[] points = [(-6.000, -5.000, 0.000), (6.000, -5.000, 0.000), (6.000, 26.000, 0.000), (-6.000, 26.000, 0.000)]",
        "int[] faceVertexCounts = [4]",
        "int[] faceVertexIndices = [0, 1, 2, 3]",
        "color3f[] primvars:displayColor = [(0.180, 0.190, 0.200)]",
    ]
    return prim("Mesh", "Floor", attributes, level=1)


def build_generator() -> str:
    looks = prim(
        "Scope",
        "Looks",
        children=[
            material("GenBlue", (0.020, 0.180, 0.420), 0.42, 0.0, level=2),
            material("MetalGrey", (0.450, 0.470, 0.480), 0.36, 0.45, level=2),
            material("RadiatorDark", (0.030, 0.035, 0.040), 0.68, 0.1, level=2),
            material("ExhaustSteel", (0.620, 0.600, 0.560), 0.31, 0.65, level=2),
            material("PanelBlack", (0.005, 0.006, 0.008), 0.5, 0.0, level=2),
            material("FluidAmber", (0.950, 0.530, 0.080), 0.22, 0.0, level=2),
            material("AlarmRed", (1.000, 0.030, 0.020), 0.25, 0.0, level=2),
            material("HaloOrange", (1.000, 0.360, 0.020), 0.18, 0.0, level=2),
            material("LampGreen", (0.050, 0.950, 0.250), 0.18, 0.0, level=2),
        ],
        level=1,
    )

    engine = prim(
        "Xform",
        "Engine",
        xform_attrs(translate=(-1.000, 0.000, 0.750)),
        [
            cube("Block", (1.600, 0.850, 0.950), (0.000, 0.000, 0.000), "GenBlue", 2),
            cylinder("Turbo", "Y", 0.160, 0.350, (-0.450, 0.500, 0.330), "MetalGrey", 2),
            sphere("OverheatHalo", 0.300, (0.000, 0.000, 0.180), "HaloOrange", 2, visible=False),
            sphere("OilHalo", 0.220, (-0.250, 0.000, -0.420), "HaloOrange", 2, visible=False),
        ],
        level=1,
    )

    alternator = prim(
        "Xform",
        "Alternator",
        xform_attrs(translate=(0.950, 0.000, 0.800)),
        [
            cylinder("Housing", "X", 0.420, 1.200, (0.000, 0.000, 0.000), "MetalGrey", 2),
            cube("TerminalBox", (0.350, 0.300, 0.280), (0.050, -0.310, 0.370), "PanelBlack", 2),
        ],
        level=1,
    )

    fan = prim(
        "Xform",
        "Fan",
        xform_attrs(translate=(0.110, 0.000, 0.000), rotate_y=90.000, rotate_z=0.000),
        [
            cylinder("Hub", "Z", 0.080, 0.100, (0.000, 0.000, 0.000), "MetalGrey", 3),
            *(blade(index, (index - 1) * 60.0) for index in range(1, 7)),
        ],
        level=2,
    )
    radiator = prim(
        "Xform",
        "Radiator",
        xform_attrs(translate=(-2.050, 0.000, 1.000)),
        [cube("Core", (0.180, 1.300, 1.300), (0.000, 0.000, 0.000), "RadiatorDark", 2, display_color=(0.030, 0.035, 0.040)), fan],
        level=1,
    )

    exhaust = prim(
        "Xform",
        "Exhaust",
        xform_attrs(translate=(-0.400, 0.450, 1.550)),
        [
            cylinder("Silencer", "X", 0.220, 1.100, (0.000, 0.000, 0.000), "ExhaustSteel", 2),
            cylinder("Stack", "Z", 0.110, 1.200, (-0.450, 0.000, 0.550), "ExhaustSteel", 2, display_color=(0.620, 0.600, 0.560)),
        ],
        level=1,
    )

    def gauge(name: str, translate: Sequence[float]) -> str:
        needle = prim(
            "Xform",
            "Needle",
            xform_attrs(translate=(0.000, -0.018, 0.000), rotate_x=90.000, rotate_z=0.000),
            [cube("Pointer", (0.012, 0.075, 0.006), (0.000, 0.040, 0.000), "AlarmRed", 4)],
            level=3,
        )
        return prim(
            "Xform",
            name,
            xform_attrs(translate=translate),
            [
                cylinder("Face", "Y", 0.110, 0.020, (0.000, 0.000, 0.000), "MetalGrey", 3),
                needle,
            ],
            level=2,
        )

    control_panel = prim(
        "Xform",
        "ControlPanel",
        xform_attrs(translate=(0.200, -0.800, 1.200)),
        [
            cube("Enclosure", (0.500, 0.120, 0.700), (0.000, 0.000, 0.000), "PanelBlack", 2),
            gauge("LoadGauge", (-0.120, -0.070, 0.120)),
            gauge("TempGauge", (0.120, -0.070, 0.120)),
            sphere("RunLamp", 0.050, (0.000, -0.070, -0.180), "LampGreen", 2, visible=False),
        ],
        level=1,
    )

    fuel_tank = prim(
        "Xform",
        "FuelTank",
        xform_attrs(translate=(0.000, 0.000, 0.405)),
        [
            cube("Shell", (2.200, 1.100, 0.550), (0.000, 0.000, 0.000), "MetalGrey", 2),
            prim(
                "Cube",
                "Surface",
                [
                    "double size = 1",
                    f"matrix4d xformOp:transform = {matrix4d_translate((0.000, 0.000, 0.165))}",
                    'uniform token[] xformOpOrder = ["xformOp:transform"]',
                    bind("FluidAmber"),
                ],
                level=2,
            ),
        ],
        level=1,
    )

    root = prim(
        "Xform",
        "Generator",
        [
            f"matrix4d xformOp:transform = {matrix4d_translate((0.000, 0.000, 0.000))}",
            'uniform token[] xformOpOrder = ["xformOp:transform"]',
        ],
        [
            looks,
            cube("Skid", (4.000, 1.500, 0.200), (0.000, 0.000, 0.100), "MetalGrey", 1),
            engine,
            alternator,
            cylinder("CouplingGuard", "X", 0.300, 0.350, (0.000, 0.000, 0.780), "MetalGrey", 1),
            radiator,
            exhaust,
            control_panel,
            fuel_tank,
            cube("Battery", (0.450, 0.300, 0.280), (-1.550, -0.420, 0.350), "PanelBlack", 1),
            annulus_mesh("AlarmRing", 2.250, 2.450, 0.020, 48, "AlarmRed"),
        ],
        metadata='kind = "component"',
        level=0,
    )
    return "\n".join(
        [
            "#usda 1.0",
            "(",
            '    doc = """Reusable 400 kW diesel generator component generated by generate_generator_assets.py."""',
            '    defaultPrim = "Generator"',
            "    metersPerUnit = 1",
            '    upAxis = "Z"',
            ")",
            "",
            root,
            "",
        ]
    )


def build_powerhouse() -> str:
    camera = prim(
        "Camera",
        "HeroCamera",
        [
            "# Operator viewpoint in front of the row; frames generator sets laid out along +Y at 6 m pitch.",
            "float focalLength = 20",
            *xform_attrs(translate=(9.500, -5.500, 1.700), rotate_xyz=(84.000, 0.000, 48.000)),
        ],
        level=1,
    )
    powerhouse = prim(
        "Xform",
        "Powerhouse",
        [
            # Declared so a site-level layer can position the whole powerhouse
            # beside the pump plant. xformOpOrder is uniform and cannot be
            # rewritten from a stronger layer, so a prim that another layer may
            # need to place has to declare the op itself - and it must be
            # xformOp:transform, because that is the single matrix op a
            # positioning layer writes. Identity here: standalone, the powerhouse
            # sits at its own origin.
            "matrix4d xformOp:transform = ( (1, 0, 0, 0), (0, 1, 0, 0), (0, 0, 1, 0), (0, 0, 0, 1) )",
            'uniform token[] xformOpOrder = ["xformOp:transform"]',
        ],
        children=[
            floor_mesh(),
            prim("DistantLight", "KeyLight", ["float inputs:intensity = 650", "float inputs:angle = 0.5", *xform_attrs(rotate_xyz=(45.000, 0.000, -35.000))], level=1),
            prim("DomeLight", "FillDome", ["float inputs:intensity = 120", "color3f inputs:color = (0.720, 0.790, 0.900)"], level=1),
            prim("Scope", "Generators", level=1),
            camera,
        ],
        level=0,
    )
    return "\n".join(
        [
            "#usda 1.0",
            "(",
            '    defaultPrim = "Powerhouse"',
            "    metersPerUnit = 1",
            '    upAxis = "Z"',
            ")",
            "",
            powerhouse,
            "",
        ]
    )


def write_file(path: Path, content: str) -> None:
    path.write_text(content, encoding="utf-8", newline="\n")
    print(f"wrote {path.name} ({path.stat().st_size} bytes)")


def main() -> None:
    output_dir = Path(__file__).resolve().parent
    write_file(output_dir / "generator.usda", build_generator())
    write_file(output_dir / "Powerhouse.usda", build_powerhouse())


if __name__ == "__main__":
    main()
