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


def _material_prim(
    name: str,
    color: Sequence[float],
    roughness: float,
    metallic: float,
    level: int,
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


# Every material's diffuse colour, keyed by name. Bound geometry also carries
# this as primvars:displayColor: a UsdPreviewSurface network is only honoured by
# renderers that evaluate material networks, and those that do not fall back to
# displayColor - without it the whole machine renders default grey and none of
# the colour work here is visible at all.
MATERIAL_COLORS: dict[str, Sequence[float]] = {}


def material(
    name: str,
    color: Sequence[float],
    roughness: float = 0.55,
    metallic: float = 0.0,
    level: int = 1,
) -> str:
    MATERIAL_COLORS[name] = color
    return _material_prim(name, color, roughness, metallic, level)


def bind(material_name: str) -> str:
    return f"rel material:binding = </Generator/Looks/{material_name}>"


def display_color_attr(
    material_name: str,
    explicit: Sequence[float] | None,
) -> list[str]:
    """Returns the displayColor attribute for a bound prim, if any is known."""
    color = explicit if explicit is not None else MATERIAL_COLORS.get(material_name)
    if color is None:
        return []
    return [
        f"color3f[] primvars:displayColor = "
        f"[({f(color[0])}, {f(color[1])}, {f(color[2])})]"
    ]


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
    rotate_xyz: Sequence[float] | None = None,
) -> str:
    attributes = ["double size = 1"]
    attributes.extend(xform_attrs(translate=translate, rotate_xyz=rotate_xyz, scale=size))
    attributes.append(bind(material_name))
    # A prim a live binding recolours must declare primvars:displayColor itself;
    # every other prim gets its material's colour there so renderers that do not
    # evaluate material networks still show the machine in colour rather than
    # default grey.
    attributes.extend(display_color_attr(material_name, display_color))
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
    rotate_xyz: Sequence[float] | None = None,
) -> str:
    attributes = [f'uniform token axis = "{axis}"', f"double radius = {f(radius)}", f"double height = {f(height)}"]
    attributes.extend(xform_attrs(translate=translate, rotate_xyz=rotate_xyz))
    attributes.append(bind(material_name))
    attributes.extend(display_color_attr(material_name, display_color))
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
    attributes.extend(display_color_attr(material_name, None))
    if not visible:
        attributes.append('token visibility = "invisible"')
    return prim("Sphere", name, attributes, level=level)



def blade(index: int, angle: float) -> str:
    attributes = ["double size = 1"]
    attributes.extend(xform_attrs(translate=(0.000, 0.260, 0.000), rotate_z=angle, scale=(0.070, 0.440, 0.025)))
    attributes.append(bind("MetalGrey"))
    attributes.extend(display_color_attr("MetalGrey", None))
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
    """Builds the reusable generator-set component.

    Modelled on an open-frame ~400 kW V16 diesel genset: channel-steel skid with
    an integral base fuel tank, radiator and guarded fan pack at one end, the
    engine amidships with its exhaust manifolds and turbochargers on top, the
    alternator drum at the other end, and the control panel on the front face.

    Prim names are load-bearing. OpenUsdBindings.cs drives Radiator/Fan,
    Radiator/Core, Exhaust/Stack, ControlPanel/LoadGauge/Needle,
    ControlPanel/TempGauge/Needle, ControlPanel/RunLamp, FuelTank/Surface,
    AlarmRing, Engine/OverheatHalo and Engine/OilHalo by path, so those names
    cannot be changed here without changing the bindings with them.
    """
    looks = prim(
        "Scope",
        "Looks",
        children=[
            # The body colour of the machine: engine, alternator and skid.
            material("GenGreen", (0.016, 0.210, 0.166), 0.38, 0.05, level=2),
            material("GenGreenDark", (0.010, 0.140, 0.112), 0.45, 0.05, level=2),
            material("MetalGrey", (0.450, 0.470, 0.480), 0.36, 0.45, level=2),
            material("SteelBright", (0.560, 0.575, 0.590), 0.22, 0.85, level=2),
            material("RadiatorDark", (0.030, 0.035, 0.040), 0.68, 0.1, level=2),
            # Exhaust manifolds run hot and scale over: a dry copper-oxide brown.
            material("ManifoldRust", (0.235, 0.088, 0.042), 0.72, 0.25, level=2),
            material("ExhaustSteel", (0.620, 0.600, 0.560), 0.31, 0.65, level=2),
            material("PanelBlack", (0.005, 0.006, 0.008), 0.5, 0.0, level=2),
            material("PanelFascia", (0.190, 0.200, 0.208), 0.55, 0.0, level=2),
            material("ScreenDark", (0.020, 0.045, 0.038), 0.28, 0.0, level=2),
            material("FilterCream", (0.760, 0.735, 0.660), 0.6, 0.0, level=2),
            material("HoseBlack", (0.028, 0.030, 0.032), 0.75, 0.0, level=2),
            material("LabelYellow", (0.780, 0.610, 0.040), 0.6, 0.0, level=2),
            material("FluidAmber", (0.950, 0.530, 0.080), 0.22, 0.0, level=2),
            material("AlarmRed", (1.000, 0.030, 0.020), 0.25, 0.0, level=2),
            material("HaloOrange", (1.000, 0.360, 0.020), 0.18, 0.0, level=2),
            material("LampGreen", (0.050, 0.950, 0.250), 0.18, 0.0, level=2),
        ],
        level=1,
    )

    # --- Skid -------------------------------------------------------------
    # Channel-section side rails with cross members, rather than a slab: the
    # rails are what a real set is craned and forklifted by, and they read as
    # the machine's outline from any angle.
    def rail(name: str, y: float) -> str:
        return prim(
            "Xform",
            name,
            xform_attrs(translate=(0.000, y, 0.000)),
            [
                cube("Web", (4.000, 0.040, 0.240), (0.000, 0.000, 0.000), "GenGreen", 3),
                cube("FlangeTop", (4.000, 0.150, 0.035), (0.000, 0.000, 0.122), "GenGreen", 3),
                cube("FlangeBottom", (4.000, 0.150, 0.035), (0.000, 0.000, -0.122), "GenGreen", 3),
            ],
            level=2,
        )

    skid = prim(
        "Xform",
        "Skid",
        xform_attrs(translate=(0.000, 0.000, 0.140)),
        [
            rail("RailLeft", 0.660),
            rail("RailRight", -0.660),
            *(
                cube(f"CrossMember{index + 1}", (0.110, 1.320, 0.180), (x, 0.000, 0.000), "GenGreen", 2)
                for index, x in enumerate((-1.860, -0.980, 0.180, 1.180, 1.880))
            ),
            # Lifting points, as on the rails of a skid-mounted set.
            *(
                cube(f"LiftLug{index + 1}", (0.120, 0.030, 0.130), (x, y, 0.160), "LabelYellow", 2)
                for index, (x, y) in enumerate(
                    ((-1.700, 0.660), (-1.700, -0.660), (1.700, 0.660), (1.700, -0.660))
                )
            ),
        ],
        level=1,
    )

    # --- Engine -----------------------------------------------------------
    # A 60-degree V16: two banks of eight, each carrying its own rocker covers,
    # exhaust manifold and turbocharger.
    bank_tilt = 30.000

    def bank(name: str, sign: float) -> str:
        y = 0.245 * sign
        covers = [
            cube(
                f"RockerCover{index + 1}",
                (0.150, 0.230, 0.090),
                (-0.620 + index * 0.180, 0.150 * sign, 0.250),
                "GenGreen",
                4,
                rotate_xyz=(bank_tilt * sign, 0.000, 0.000),
            )
            for index in range(8)
        ]
        return prim(
            "Xform",
            name,
            xform_attrs(translate=(0.000, y, 0.190)),
            [
                cube(
                    "Head",
                    (1.560, 0.300, 0.360),
                    (0.000, 0.100 * sign, 0.120),
                    "GenGreen",
                    4,
                    rotate_xyz=(bank_tilt * sign, 0.000, 0.000),
                ),
                *covers,
            ],
            level=3,
        )

    def manifold(name: str, sign: float) -> str:
        return prim(
            "Xform",
            name,
            xform_attrs(translate=(0.000, 0.430 * sign, 0.560)),
            [
                cylinder("Log", "X", 0.062, 1.500, (0.000, 0.000, 0.000), "ManifoldRust", 4, display_color=(0.235, 0.088, 0.042)),
                *(
                    cylinder(
                        f"Riser{index + 1}",
                        "Z",
                        0.038,
                        0.150,
                        (-0.620 + index * 0.180, 0.000, 0.090),
                        "ManifoldRust",
                        4,
                    )
                    for index in range(8)
                ),
                # The elbow that carries the bank into its turbocharger.
                cylinder("Elbow", "Z", 0.072, 0.300, (0.820, 0.000, 0.180), "ManifoldRust", 4),
                cylinder(
                    "ElbowBend",
                    "X",
                    0.072,
                    0.240,
                    (0.930, 0.000, 0.320),
                    "ManifoldRust",
                    4,
                ),
            ],
            level=3,
        )

    def turbo(name: str, sign: float) -> str:
        return prim(
            "Xform",
            name,
            xform_attrs(translate=(1.070, 0.330 * sign, 0.880), rotate_x=0.000),
            [
                # Turbine housing (hot side) and compressor housing (cold side).
                cylinder("TurbineHousing", "X", 0.135, 0.150, (-0.090, 0.000, 0.000), "PanelBlack", 4),
                cylinder("Cartridge", "X", 0.070, 0.130, (0.020, 0.000, 0.000), "SteelBright", 4),
                cylinder("CompressorHousing", "X", 0.150, 0.160, (0.150, 0.000, 0.000), "SteelBright", 4),
                cylinder("Inlet", "Y", 0.105, 0.150, (0.150, 0.130 * sign, 0.000), "SteelBright", 4),
            ],
            level=3,
        )

    def intake(name: str, sign: float) -> str:
        # Charge-air pipe arcing from the aftercooler down into the bank.
        return prim(
            "Xform",
            name,
            xform_attrs(translate=(0.000, 0.000, 0.000)),
            [
                cylinder("Riser", "Z", 0.090, 0.360, (0.700, 0.560 * sign, 1.000), "GenGreen", 4),
                cylinder("Crossover", "X", 0.090, 0.900, (0.220, 0.560 * sign, 1.170), "GenGreen", 4),
                cylinder("Drop", "Z", 0.085, 0.300, (-0.250, 0.560 * sign, 1.020), "GenGreen", 4),
            ],
            level=3,
        )

    engine = prim(
        "Xform",
        "Engine",
        xform_attrs(translate=(-0.480, 0.000, 0.780)),
        [
            # Oil pan, crankcase and the front gear case.
            cube("Sump", (1.720, 0.760, 0.240), (0.000, 0.000, -0.300), "GenGreenDark", 3),
            cube("Crankcase", (1.780, 0.900, 0.400), (0.000, 0.000, -0.030), "GenGreen", 3),
            cube("GearCase", (0.180, 0.820, 0.560), (-0.960, 0.000, 0.060), "GenGreen", 3),
            cube("Flywheel", (0.240, 0.780, 0.780), (0.980, 0.000, 0.030), "GenGreen", 3),
            bank("BankLeft", 1.0),
            bank("BankRight", -1.0),
            manifold("ManifoldLeft", 1.0),
            manifold("ManifoldRight", -1.0),
            turbo("TurboLeft", 1.0),
            turbo("TurboRight", -1.0),
            intake("IntakeLeft", 1.0),
            intake("IntakeRight", -1.0),
            # Aftercooler sits in the vee, between the banks.
            cube("Aftercooler", (0.900, 0.420, 0.240), (0.150, 0.000, 0.620), "GenGreen", 3),
            cube("ValleyCover", (1.300, 0.300, 0.100), (-0.350, 0.000, 0.480), "GenGreen", 3),
            # Spin-on filter bank on the service side, as on the photographs.
            *(
                cylinder(
                    f"FuelFilter{index + 1}",
                    "Z",
                    0.055,
                    0.230,
                    (-0.250 + index * 0.145, -0.560, -0.120),
                    "FilterCream",
                    3,
                )
                for index in range(5)
            ),
            *(
                cylinder(
                    f"OilFilter{index + 1}",
                    "Z",
                    0.075,
                    0.300,
                    (0.480 + index * 0.190, -0.540, -0.100),
                    "FilterCream",
                    3,
                )
                for index in range(2)
            ),
            cylinder("StarterMotor", "X", 0.110, 0.420, (0.720, 0.470, -0.240), "PanelBlack", 3),
            cylinder("ChargeAlternator", "X", 0.090, 0.260, (-0.700, -0.470, 0.140), "SteelBright", 3),
            cylinder("WaterPump", "X", 0.130, 0.200, (-1.020, -0.300, -0.140), "GenGreen", 3),
            # Coolant hoses to the radiator.
            cylinder("HoseTop", "X", 0.070, 0.520, (-1.320, 0.240, 0.240), "HoseBlack", 3),
            cylinder("HoseBottom", "X", 0.070, 0.520, (-1.320, -0.240, -0.220), "HoseBlack", 3),
            # Fault indicators sit clear of the machine on purpose. Both used to be
            # tucked into the engine centre, which was fine against a plain block
            # but is invisible now that there is a crankcase, a vee full of
            # aftercooler and a sump in the way - an indicator you cannot see is
            # worse than none, because it reads as "no fault".
            sphere("OverheatHalo", 0.340, (0.000, 0.000, 1.380), "HaloOrange", 3, visible=False),
            sphere("OilHalo", 0.260, (-0.100, -0.820, -0.280), "HaloOrange", 3, visible=False),
        ],
        level=1,
    )

    # --- Alternator -------------------------------------------------------
    louvres = [
        cube(
            f"Louvre{index + 1}",
            (0.030, 0.120, 0.500),
            (-0.360 + index * 0.075, 0.000, 0.000),
            "GenGreenDark",
            3,
            rotate_xyz=(0.000, 0.000, 0.000),
        )
        for index in range(9)
    ]
    alternator = prim(
        "Xform",
        "Alternator",
        xform_attrs(translate=(1.320, 0.000, 0.860)),
        [
            cylinder("Housing", "X", 0.440, 1.120, (0.000, 0.000, 0.000), "GenGreen", 2),
            # Ventilation slots around the barrel.
            prim(
                "Xform",
                "VentBand",
                xform_attrs(translate=(0.000, 0.000, 0.430)),
                louvres,
                level=2,
            ),
            cylinder("DriveEndBell", "X", 0.400, 0.140, (-0.610, 0.000, 0.000), "GenGreenDark", 2),
            cylinder("NonDriveEndBell", "X", 0.360, 0.160, (0.620, 0.000, 0.000), "GenGreenDark", 2),
            # A band around the barrel that carries winding heat. The alternator
            # is the one major assembly with no moving part a viewer can see, so
            # without this it is the only thing on the machine that never reacts.
            cylinder(
                "HeatBand",
                "X",
                0.455,
                0.220,
                (-0.180, 0.000, 0.000),
                "GenGreenDark",
                2,
                display_color=(0.016, 0.210, 0.166),
            ),
            cube("TerminalBox", (0.480, 0.420, 0.300), (0.100, 0.000, 0.520), "GenGreen", 2),
            cube("TerminalLid", (0.500, 0.440, 0.030), (0.100, 0.000, 0.685), "GenGreenDark", 2),
            cube("FootLeft", (0.700, 0.120, 0.220), (0.000, 0.400, -0.520), "GenGreen", 2),
            cube("FootRight", (0.700, 0.120, 0.220), (0.000, -0.400, -0.520), "GenGreen", 2),
        ],
        level=1,
    )

    # --- Radiator ---------------------------------------------------------
    fan = prim(
        "Xform",
        "Fan",
        xform_attrs(translate=(0.180, 0.000, 0.000), rotate_y=90.000, rotate_z=0.000),
        [
            cylinder("Hub", "Z", 0.110, 0.120, (0.000, 0.000, 0.000), "PanelBlack", 3),
            *(blade(index, (index - 1) * 40.0) for index in range(1, 10)),
        ],
        level=2,
    )
    # Vertical fin pack: what actually reads as a radiator at a distance.
    fins = [
        cube(
            f"Fin{index + 1}",
            (0.012, 1.180, 1.180),
            (-0.075 + (index % 2) * 0.150, -0.620 + index * 0.083, 0.000),
            "RadiatorDark",
            3,
        )
        for index in range(15)
    ]
    radiator = prim(
        "Xform",
        "Radiator",
        xform_attrs(translate=(-2.020, 0.000, 1.020)),
        [
            cube(
                "Core",
                (0.200, 1.280, 1.280),
                (0.000, 0.000, 0.000),
                "RadiatorDark",
                2,
                display_color=(0.030, 0.035, 0.040),
            ),
            prim("Xform", "FinPack", xform_attrs(translate=(0.000, 0.000, 0.000)), fins, level=2),
            # Bolted guard frame and header tanks.
            cube("TopTank", (0.280, 1.360, 0.170), (0.000, 0.000, 0.700), "PanelBlack", 2),
            cube("BottomTank", (0.280, 1.360, 0.170), (0.000, 0.000, -0.700), "PanelBlack", 2),
            cube("GuardLeft", (0.300, 0.070, 1.560), (0.000, 0.680, 0.000), "PanelBlack", 2),
            cube("GuardRight", (0.300, 0.070, 1.560), (0.000, -0.680, 0.000), "PanelBlack", 2),
            cube("FillerCap", (0.140, 0.140, 0.090), (0.000, 0.480, 0.820), "SteelBright", 2),
            cylinder("FanShroud", "X", 0.560, 0.120, (0.220, 0.000, 0.000), "PanelBlack", 2),
            fan,
        ],
        level=1,
    )

    # --- Exhaust ----------------------------------------------------------
    exhaust = prim(
        "Xform",
        "Exhaust",
        xform_attrs(translate=(-0.100, 0.430, 1.560)),
        [
            cylinder("Silencer", "X", 0.230, 1.150, (0.000, 0.000, 0.000), "ExhaustSteel", 2),
            cylinder("EndCapFront", "X", 0.235, 0.060, (-0.600, 0.000, 0.000), "ExhaustSteel", 2),
            cylinder("EndCapRear", "X", 0.235, 0.060, (0.600, 0.000, 0.000), "ExhaustSteel", 2),
            cylinder("Bellows", "Z", 0.090, 0.220, (0.560, 0.000, -0.280), "SteelBright", 2),
            cylinder(
                "Stack",
                "Z",
                0.115,
                1.250,
                (-0.450, 0.000, 0.600),
                "ExhaustSteel",
                2,
                display_color=(0.620, 0.600, 0.560),
            ),
            cylinder("RainCap", "Z", 0.140, 0.060, (-0.450, 0.000, 1.250), "ExhaustSteel", 2),
        ],
        level=1,
    )

    # --- Control panel ----------------------------------------------------
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
                cylinder("Face", "Y", 0.075, 0.020, (0.000, 0.000, 0.000), "MetalGrey", 3),
                cylinder("Bezel", "Y", 0.085, 0.012, (0.000, 0.008, 0.000), "PanelBlack", 3),
                needle,
            ],
            level=2,
        )

    keypad = [
        cube(
            f"Key{index + 1}",
            (0.036, 0.014, 0.026),
            (-0.075 + (index % 4) * 0.050, -0.072, -0.050 - (index // 4) * 0.042),
            "PanelFascia",
            3,
        )
        for index in range(8)
    ]

    control_panel = prim(
        "Xform",
        "ControlPanel",
        xform_attrs(translate=(1.180, -0.720, 1.500)),
        [
            # Sheet-metal cabinet in body colour, with a dark instrument fascia
            # recessed into its front - the arrangement in the close-up photo.
            cube("Enclosure", (0.640, 0.260, 0.760), (0.000, 0.000, 0.000), "GenGreen", 2),
            cube("Fascia", (0.560, 0.030, 0.640), (0.000, -0.135, 0.000), "PanelBlack", 2),
            cube("Display", (0.230, 0.016, 0.180), (0.010, -0.155, 0.120), "ScreenDark", 2),
            cube("MimicPlate", (0.150, 0.014, 0.300), (-0.190, -0.152, 0.060), "PanelFascia", 2),
            prim("Xform", "Keypad", xform_attrs(translate=(0.010, 0.000, 0.120)), keypad, level=2),
            # Emergency stop: the red mushroom every panel carries.
            cylinder("EStopBase", "Y", 0.055, 0.030, (0.190, -0.150, -0.230), "LabelYellow", 2),
            cylinder("EStopButton", "Y", 0.042, 0.045, (0.190, -0.175, -0.230), "AlarmRed", 2),
            cube("LabelStrip", (0.520, 0.012, 0.070), (0.000, -0.150, -0.330), "LabelYellow", 2),
            gauge("LoadGauge", (-0.150, -0.150, 0.250)),
            gauge("TempGauge", (0.150, -0.150, 0.250)),
            sphere("RunLamp", 0.030, (-0.190, -0.160, -0.230), "LampGreen", 2, visible=False),
        ],
        level=1,
    )

    # --- Base fuel tank ---------------------------------------------------
    # The set carries its fuel in the skid, which is why the rails are so deep.
    fuel_tank = prim(
        "Xform",
        "FuelTank",
        xform_attrs(translate=(0.000, 0.000, 0.400)),
        [
            cube("Shell", (3.700, 1.240, 0.430), (0.000, 0.000, 0.000), "GenGreen", 2),
            cube("Strap1", (0.070, 1.260, 0.450), (-1.200, 0.000, 0.000), "GenGreenDark", 2),
            cube("Strap2", (0.070, 1.260, 0.450), (1.200, 0.000, 0.000), "GenGreenDark", 2),
            cylinder("FillerNeck", "Z", 0.070, 0.120, (-1.650, -0.480, 0.250), "SteelBright", 2),
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

    battery = prim(
        "Xform",
        "BatteryRack",
        xform_attrs(translate=(-1.560, -0.430, 0.420)),
        [
            cube("Tray", (0.520, 0.340, 0.040), (0.000, 0.000, -0.140), "GenGreenDark", 2),
            cube("BatteryA", (0.230, 0.300, 0.240), (-0.130, 0.000, 0.000), "PanelBlack", 2),
            cube("BatteryB", (0.230, 0.300, 0.240), (0.130, 0.000, 0.000), "PanelBlack", 2),
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
            skid,
            fuel_tank,
            engine,
            alternator,
            cylinder("CouplingGuard", "X", 0.330, 0.420, (0.660, 0.000, 0.830), "GenGreenDark", 1),
            radiator,
            exhaust,
            control_panel,
            battery,
            # A beacon ring above the set rather than a decal on the floor. On the
            # floor it was hidden by the skid and the neighbouring machines from
            # any operator-height camera, which made the one indicator that says
            # "this machine has tripped" the hardest thing in the scene to see.
            annulus_mesh("AlarmRing", 1.500, 1.900, 3.100, 48, "AlarmRed"),
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
