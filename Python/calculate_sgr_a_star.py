#!/usr/bin/env python3
"""Calculate Sagittarius A* coordinates for the solar system dataset."""

# Sagittarius A* coordinates (J2000)
# RA: 17h 45m 40.04s = 266.41683 degrees
# Dec: -29° 00′ 28.12″ = -29.00781 degrees
ra_deg = 266.41683
dec_deg = -29.00781

# Distance to Sgr A* (galactic center) is approximately 8.178 kpc
distance_pc = 8178.0  # parsecs (26,670 light years)

# For stars, parallax_mas = 1000 / distance_pc
parallax_mas = 1000.0 / distance_pc

print("Sagittarius A* (Galactic Center Black Hole)")
print(f"RA: {ra_deg:.9f} degrees")
print(f"Dec: {dec_deg:.8f} degrees")
print(f"Distance: {distance_pc:.1f} parsecs ({distance_pc/1000:.3f} kpc, {distance_pc * 3.262:.0f} light years)")
print(f"Parallax: {parallax_mas:.6f} milliarcseconds")
print()
print("CSV line to add:")
print(f"9000000000,black_hole,{ra_deg:.9f},{dec_deg:.8f},{parallax_mas:.12f},{distance_pc:.15e},-10.0,,,0.000000000000e+00,0.000000000000e+00,0.000000000000e+00,0.0,,,,,,,")
