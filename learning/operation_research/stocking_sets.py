from ortools.linear_solver import pywraplp

# ==========================================================
# Create Solver
# ==========================================================

solver = pywraplp.Solver.CreateSolver("SCIP")

if solver is None:
    raise RuntimeError("GLOP solver is unavailable.")

print(f"Solver: {solver.SolverVersion()}")

# ==========================================================
# Decision Variables
# ==========================================================

infinity = solver.infinity()
M = 10000


# sets
set1 = solver.BoolVar("set1")
set2 = solver.BoolVar("set2")
set3 = solver.BoolVar("set3")
set4 = solver.BoolVar("set4")
set5 = solver.BoolVar("set5")
set6 = solver.BoolVar("set6")
set7 = solver.BoolVar("set7")
set8 = solver.BoolVar("set8")
set9 = solver.BoolVar("set9")
set10 = solver.BoolVar("set10")
set11 = solver.BoolVar("set11")
set12 = solver.BoolVar("set12")
set13 = solver.BoolVar("set13")
set14 = solver.BoolVar("set14")
set15 = solver.BoolVar("set15")
set16 = solver.BoolVar("set16")
set17 = solver.BoolVar("set17")
set18 = solver.BoolVar("set18")
set19 = solver.BoolVar("set19")
set20 = solver.BoolVar("set20")

# features
T1 = solver.BoolVar("T1")
T2 = solver.BoolVar("T2")
T3 = solver.BoolVar("T3")
T4 = solver.BoolVar("T4")

L1 = solver.BoolVar("L1")
L2 = solver.BoolVar("L2")
L3 = solver.BoolVar("L3")
L4 = solver.BoolVar("L4")

W1 = solver.BoolVar("W1")
W2 = solver.BoolVar("W2")
W3 = solver.BoolVar("W3")
W4 = solver.BoolVar("W4")

C1 = solver.BoolVar("C1")
C2 = solver.BoolVar("C2")
C3 = solver.BoolVar("C3")
C4 = solver.BoolVar("C4")

O1 = solver.BoolVar("O1")
O2 = solver.BoolVar("O2")
O3 = solver.BoolVar("O3")
O4 = solver.BoolVar("O4")

S1 = solver.BoolVar("S1")
S2 = solver.BoolVar("S2")
S3 = solver.BoolVar("S3")
S4 = solver.BoolVar("S4")

R1 = solver.BoolVar("R1")
R2 = solver.BoolVar("R2")
R3 = solver.BoolVar("R3")
R4 = solver.BoolVar("R4")

D1 = solver.BoolVar("D1")
D2 = solver.BoolVar("D2")




# ==========================================================
# Constraints
# ==========================================================

# constraint 1
c1 = solver.Constraint(-infinity, 2, "c1")
c1.SetCoefficient(T1, 1)
c1.SetCoefficient(T2, 1)
c1.SetCoefficient(T3, 1)
c1.SetCoefficient(T4, 1)

# constraint 1
c2 = solver.Constraint(-infinity, 2, "c2")
c2.SetCoefficient(L1, 1)
c2.SetCoefficient(L2, 1)
c2.SetCoefficient(L3, 1)
c2.SetCoefficient(L4, 1)

# constraint 1
c3 = solver.Constraint(-infinity, 2, "c3")
c3.SetCoefficient(W1, 1)
c3.SetCoefficient(W2, 1)
c3.SetCoefficient(W3, 1)
c3.SetCoefficient(W4, 1)

# constraint 1
c4 = solver.Constraint(-infinity, 2, "c4")
c4.SetCoefficient(C1, 1)
c4.SetCoefficient(C2, 1)
c4.SetCoefficient(C3, 1)
c4.SetCoefficient(C4, 1)

# constraint 1
c5 = solver.Constraint(-infinity, 3, "c5")
c5.SetCoefficient(O1, 1)
c5.SetCoefficient(O2, 1)
c5.SetCoefficient(O3, 1)
c5.SetCoefficient(O4, 1)

# constraint 1
c6 = solver.Constraint(-infinity, 2, "c6")
c6.SetCoefficient(S1, 1)
c6.SetCoefficient(S2, 1)
c6.SetCoefficient(S3, 1)
c6.SetCoefficient(S4, 1)

# constraint 1
c7 = solver.Constraint(-infinity, 2, "c7")
c7.SetCoefficient(R1, 1)
c7.SetCoefficient(R2, 1)
c7.SetCoefficient(R3, 1)
c7.SetCoefficient(R4, 1)
c7.SetCoefficient(D1, 1)
c7.SetCoefficient(D2, 1)


c8 = solver.Constraint(-infinity, 7, "c8")
c8.SetCoefficient(set1, -1)
c8.SetCoefficient(T2, 1)
c8.SetCoefficient(W2, 1)
c8.SetCoefficient(L4, 1)
c8.SetCoefficient(C2, 1)
c8.SetCoefficient(O4, 1)
c8.SetCoefficient(S2, 1)
c8.SetCoefficient(D2, 1)
c8.SetCoefficient(R2, 1)

c9 = solver.Constraint(-infinity, 7, "c9")
c9.SetCoefficient(set2, -1)
c9.SetCoefficient(T2, 1)
c9.SetCoefficient(W1, 1)
c9.SetCoefficient(L1, 1)
c9.SetCoefficient(C4, 1)
c9.SetCoefficient(O4, 1)
c9.SetCoefficient(S4, 1)
c9.SetCoefficient(D2, 1)
c9.SetCoefficient(R2, 1)

c10 = solver.Constraint(-infinity, 7, "c10")
c10.SetCoefficient(set3, -1)
c10.SetCoefficient(T1, 1)
c10.SetCoefficient(W3, 1)
c10.SetCoefficient(L2, 1)
c10.SetCoefficient(C1, 1)
c10.SetCoefficient(O1, 1)
c10.SetCoefficient(S3, 1)
c10.SetCoefficient(D1, 1)
c10.SetCoefficient(R3, 1)

c11 = solver.Constraint(-infinity, 7, "c11")
c11.SetCoefficient(set4, -1)
c11.SetCoefficient(T3, 1)
c11.SetCoefficient(W3, 1)
c11.SetCoefficient(L3, 1)
c11.SetCoefficient(C3, 1)
c11.SetCoefficient(O3, 1)
c11.SetCoefficient(S1, 1)
c11.SetCoefficient(D1, 1)
c11.SetCoefficient(R1, 1)

c12 = solver.Constraint(-infinity, 7, "c12")
c12.SetCoefficient(set5, -1)
c12.SetCoefficient(T4, 1)
c12.SetCoefficient(W4, 1)
c12.SetCoefficient(L1, 1)
c12.SetCoefficient(C1, 1)
c12.SetCoefficient(O2, 1)
c12.SetCoefficient(S2, 1)
c12.SetCoefficient(D1, 1)
c12.SetCoefficient(R1, 1)

c13 = solver.Constraint(-infinity, 7, "c13")
c13.SetCoefficient(set6, -1)
c13.SetCoefficient(T2, 1)
c13.SetCoefficient(W2, 1)
c13.SetCoefficient(L2, 1)
c13.SetCoefficient(C4, 1)
c13.SetCoefficient(O4, 1)
c13.SetCoefficient(S3, 1)
c13.SetCoefficient(D2, 1)
c13.SetCoefficient(R4, 1)

c14 = solver.Constraint(-infinity, 7, "c14")
c14.SetCoefficient(set7, -1)
c14.SetCoefficient(T1, 1)
c14.SetCoefficient(W3, 1)
c14.SetCoefficient(L4, 1)
c14.SetCoefficient(C3, 1)
c14.SetCoefficient(O2, 1)
c14.SetCoefficient(S1, 1)
c14.SetCoefficient(D1, 1)
c14.SetCoefficient(R1, 1)

c15 = solver.Constraint(-infinity, 7, "c15")
c15.SetCoefficient(set8, -1)
c15.SetCoefficient(T2, 1)
c15.SetCoefficient(W1, 1)
c15.SetCoefficient(L3, 1)
c15.SetCoefficient(C1, 1)
c15.SetCoefficient(O1, 1)
c15.SetCoefficient(S3, 1)
c15.SetCoefficient(D2, 1)
c15.SetCoefficient(R4, 1)

c16 = solver.Constraint(-infinity, 7, "c16")
c16.SetCoefficient(set9, -1)
c16.SetCoefficient(T2, 1)
c16.SetCoefficient(W1, 1)
c16.SetCoefficient(L2, 1)
c16.SetCoefficient(C3, 1)
c16.SetCoefficient(O2, 1)
c16.SetCoefficient(S2, 1)
c16.SetCoefficient(D2, 1)
c16.SetCoefficient(R2, 1)

c17 = solver.Constraint(-infinity, 7, "c17")
c17.SetCoefficient(set10, -1)
c17.SetCoefficient(T1, 1)
c17.SetCoefficient(W1, 1)
c17.SetCoefficient(L1, 1)
c17.SetCoefficient(C1, 1)
c17.SetCoefficient(O3, 1)
c17.SetCoefficient(S4, 1)
c17.SetCoefficient(D1, 1)
c17.SetCoefficient(R3, 1)

c18 = solver.Constraint(-infinity, 7, "c18")
c18.SetCoefficient(set11, -1)
c18.SetCoefficient(T3, 1)
c18.SetCoefficient(W1, 1)
c18.SetCoefficient(L3, 1)
c18.SetCoefficient(C3, 1)
c18.SetCoefficient(O1, 1)
c18.SetCoefficient(S1, 1)
c18.SetCoefficient(D1, 1)
c18.SetCoefficient(R3, 1)

c19 = solver.Constraint(-infinity, 7, "c19")
c19.SetCoefficient(set12, -1)
c19.SetCoefficient(T2, 1)
c19.SetCoefficient(W2, 1)
c19.SetCoefficient(L1, 1)
c19.SetCoefficient(C2, 1)
c19.SetCoefficient(O2, 1)
c19.SetCoefficient(S4, 1)
c19.SetCoefficient(D2, 1)
c19.SetCoefficient(R2, 1)

c20 = solver.Constraint(-infinity, 7, "c20")
c20.SetCoefficient(set13, -1)
c20.SetCoefficient(T4, 1)
c20.SetCoefficient(W4, 1)
c20.SetCoefficient(L3, 1)
c20.SetCoefficient(C3, 1)
c20.SetCoefficient(O1, 1)
c20.SetCoefficient(S2, 1)
c20.SetCoefficient(D1, 1)
c20.SetCoefficient(R3, 1)

c21 = solver.Constraint(-infinity, 6, "c21")
c21.SetCoefficient(set14, -1)
c21.SetCoefficient(T4, 1)
c21.SetCoefficient(W4, 1)
c21.SetCoefficient(L4, 1)
c21.SetCoefficient(C1, 1)
c21.SetCoefficient(O3, 1)
c21.SetCoefficient(S1, 1)
c21.SetCoefficient(R1, 1)

c22 = solver.Constraint(-infinity, 6, "c22")
c22.SetCoefficient(set15, -1)
c22.SetCoefficient(T3, 1)
c22.SetCoefficient(W3, 1)
c22.SetCoefficient(L1, 1)
c22.SetCoefficient(C1, 1)
c22.SetCoefficient(O1, 1)
c22.SetCoefficient(S3, 1)
c22.SetCoefficient(R3, 1)

c23 = solver.Constraint(-infinity, 6, "c23")
c23.SetCoefficient(set16, -1)
c23.SetCoefficient(T3, 1)
c23.SetCoefficient(W3, 1)
c23.SetCoefficient(L4, 1)
c23.SetCoefficient(C1, 1)
c23.SetCoefficient(O3, 1)
c23.SetCoefficient(S2, 1)
c23.SetCoefficient(R1, 1)

c24 = solver.Constraint(-infinity, 6, "c24")
c24.SetCoefficient(set17, -1)
c24.SetCoefficient(T1, 1)
c24.SetCoefficient(W4, 1)
c24.SetCoefficient(L2, 1)
c24.SetCoefficient(C3, 1)
c24.SetCoefficient(O3, 1)
c24.SetCoefficient(S4, 1)
c24.SetCoefficient(R3, 1)

c25 = solver.Constraint(-infinity, 6, "c25")
c25.SetCoefficient(set18, -1)
c25.SetCoefficient(T2, 1)
c25.SetCoefficient(W3, 1)
c25.SetCoefficient(L3, 1)
c25.SetCoefficient(C2, 1)
c25.SetCoefficient(O4, 1)
c25.SetCoefficient(S1, 1)
c25.SetCoefficient(R2, 1)

c26 = solver.Constraint(-infinity, 6, "c26")
c26.SetCoefficient(set19, -1)
c26.SetCoefficient(T2, 1)
c26.SetCoefficient(W4, 1)
c26.SetCoefficient(L4, 1)
c26.SetCoefficient(C4, 1)
c26.SetCoefficient(O4, 1)
c26.SetCoefficient(S2, 1)
c26.SetCoefficient(R4, 1)

c27 = solver.Constraint(-infinity, 6, "c27")
c27.SetCoefficient(set20, -1)
c27.SetCoefficient(T2, 1)
c27.SetCoefficient(W3, 1)
c27.SetCoefficient(L1, 1)
c27.SetCoefficient(C1, 1)
c27.SetCoefficient(O2, 1)
c27.SetCoefficient(S3, 1)
c27.SetCoefficient(R4, 1)


c28 = solver.Constraint(0, infinity, "c28")
c28.SetCoefficient(set1, -1)
c28.SetCoefficient(T2, 1)

c281 = solver.Constraint(0, infinity, "c281")
c281.SetCoefficient(set1, -1)
c281.SetCoefficient(W2, 1)

c282 = solver.Constraint(0, infinity, "c282")
c282.SetCoefficient(set1, -1)
c282.SetCoefficient(L4, 1)

c283 = solver.Constraint(0, infinity, "c283")
c283.SetCoefficient(set1, -1)
c283.SetCoefficient(C2, 1)

c284 = solver.Constraint(0, infinity, "c284")
c284.SetCoefficient(set1, -1)
c284.SetCoefficient(O4, 1)

c285 = solver.Constraint(0, infinity, "c285")
c285.SetCoefficient(set1, -1)
c285.SetCoefficient(S2, 1)

c286 = solver.Constraint(0, infinity, "c286")
c286.SetCoefficient(set1, -1)
c286.SetCoefficient(D2, 1)

c287 = solver.Constraint(0, infinity, "c287")
c287.SetCoefficient(set1, -1)
c287.SetCoefficient(R2, 1)

c29 = solver.Constraint(0, infinity, "c29")
c29.SetCoefficient(set2, -1)
c29.SetCoefficient(T2, 1)

c291 = solver.Constraint(0, infinity, "c291")
c291.SetCoefficient(set2, -1)
c291.SetCoefficient(W1, 1)

c292 = solver.Constraint(0, infinity, "c292")
c292.SetCoefficient(set2, -1)
c292.SetCoefficient(L1, 1)

c293 = solver.Constraint(0, infinity, "c293")
c293.SetCoefficient(set2, -1)
c293.SetCoefficient(C4, 1)

c294 = solver.Constraint(0, infinity, "c294")
c294.SetCoefficient(set2, -1)
c294.SetCoefficient(O4, 1)

c295 = solver.Constraint(0, infinity, "c295")
c295.SetCoefficient(set2, -1)
c295.SetCoefficient(S4, 1)

c296 = solver.Constraint(0, infinity, "c296")
c296.SetCoefficient(set2, -1)
c296.SetCoefficient(D2, 1)

c297 = solver.Constraint(0, infinity, "c297")
c297.SetCoefficient(set2, -1)
c297.SetCoefficient(R2, 1)

c30 = solver.Constraint(0, infinity, "c30")
c30.SetCoefficient(set3, -1)
c30.SetCoefficient(T1, 1)

c301 = solver.Constraint(0, infinity, "c301")
c301.SetCoefficient(set3, -1)
c301.SetCoefficient(W3, 1)

c302 = solver.Constraint(0, infinity, "c302")
c302.SetCoefficient(set3, -1)
c302.SetCoefficient(L2, 1)

c303 = solver.Constraint(0, infinity, "c303")
c303.SetCoefficient(set3, -1)
c303.SetCoefficient(C1, 1)

c304 = solver.Constraint(0, infinity, "c304")
c304.SetCoefficient(set3, -1)
c304.SetCoefficient(O1, 1)

c305 = solver.Constraint(0, infinity, "c305")
c305.SetCoefficient(set3, -1)
c305.SetCoefficient(S3, 1)

c306 = solver.Constraint(0, infinity, "c306")
c306.SetCoefficient(set3, -1)
c306.SetCoefficient(D1, 1)

c307 = solver.Constraint(0, infinity, "c307")
c307.SetCoefficient(set3, -1)
c307.SetCoefficient(R3, 1)

c31 = solver.Constraint(0, infinity, "c31")
c31.SetCoefficient(set4, -1)
c31.SetCoefficient(T3, 1)

c311 = solver.Constraint(0, infinity, "c311")
c311.SetCoefficient(set4, -1)
c311.SetCoefficient(W3, 1)

c312 = solver.Constraint(0, infinity, "c312")
c312.SetCoefficient(set4, -1)
c312.SetCoefficient(L3, 1)

c313 = solver.Constraint(0, infinity, "c313")
c313.SetCoefficient(set4, -1)
c313.SetCoefficient(C3, 1)

c314 = solver.Constraint(0, infinity, "c314")
c314.SetCoefficient(set4, -1)
c314.SetCoefficient(O3, 1)

c315 = solver.Constraint(0, infinity, "c315")
c315.SetCoefficient(set4, -1)
c315.SetCoefficient(S1, 1)

c316 = solver.Constraint(0, infinity, "c316")
c316.SetCoefficient(set4, -1)
c316.SetCoefficient(D1, 1)

c317 = solver.Constraint(0, infinity, "c317")
c317.SetCoefficient(set4, -1)
c317.SetCoefficient(R1, 1)

c32 = solver.Constraint(0, infinity, "c32")
c32.SetCoefficient(set5, -1)
c32.SetCoefficient(T4, 1)

c321 = solver.Constraint(0, infinity, "c321")
c321.SetCoefficient(set5, -1)
c321.SetCoefficient(W4, 1)

c322 = solver.Constraint(0, infinity, "c322")
c322.SetCoefficient(set5, -1)
c322.SetCoefficient(L1, 1)

c323 = solver.Constraint(0, infinity, "c323")
c323.SetCoefficient(set5, -1)
c323.SetCoefficient(C1, 1)

c324 = solver.Constraint(0, infinity, "c324")
c324.SetCoefficient(set5, -1)
c324.SetCoefficient(O2, 1)

c325 = solver.Constraint(0, infinity, "c325")
c325.SetCoefficient(set5, -1)
c325.SetCoefficient(S2, 1)

c326 = solver.Constraint(0, infinity, "c326")
c326.SetCoefficient(set5, -1)
c326.SetCoefficient(D1, 1)

c327 = solver.Constraint(0, infinity, "c327")
c327.SetCoefficient(set5, -1)
c327.SetCoefficient(R1, 1)

c33 = solver.Constraint(0, infinity, "c33")
c33.SetCoefficient(set6, -1)
c33.SetCoefficient(T2, 1)

c331 = solver.Constraint(0, infinity, "c331")
c331.SetCoefficient(set6, -1)
c331.SetCoefficient(W2, 1)

c332 = solver.Constraint(0, infinity, "c332")
c332.SetCoefficient(set6, -1)
c332.SetCoefficient(L2, 1)

c333 = solver.Constraint(0, infinity, "c333")
c333.SetCoefficient(set6, -1)
c333.SetCoefficient(C4, 1)

c334 = solver.Constraint(0, infinity, "c334")
c334.SetCoefficient(set6, -1)
c334.SetCoefficient(O4, 1)

c335 = solver.Constraint(0, infinity, "c335")
c335.SetCoefficient(set6, -1)
c335.SetCoefficient(S3, 1)

c336 = solver.Constraint(0, infinity, "c336")
c336.SetCoefficient(set6, -1)
c336.SetCoefficient(D2, 1)

c337 = solver.Constraint(0, infinity, "c337")
c337.SetCoefficient(set6, -1)
c337.SetCoefficient(R4, 1)

c34 = solver.Constraint(0, infinity, "c34")
c34.SetCoefficient(set7, -1)
c34.SetCoefficient(T1, 1)

c341 = solver.Constraint(0, infinity, "c341")
c341.SetCoefficient(set7, -1)
c341.SetCoefficient(W3, 1)

c342 = solver.Constraint(0, infinity, "c342")
c342.SetCoefficient(set7, -1)
c342.SetCoefficient(L4, 1)

c343 = solver.Constraint(0, infinity, "c343")
c343.SetCoefficient(set7, -1)
c343.SetCoefficient(C3, 1)

c344 = solver.Constraint(0, infinity, "c344")
c344.SetCoefficient(set7, -1)
c344.SetCoefficient(O2, 1)

c345 = solver.Constraint(0, infinity, "c345")
c345.SetCoefficient(set7, -1)
c345.SetCoefficient(S1, 1)

c346 = solver.Constraint(0, infinity, "c346")
c346.SetCoefficient(set7, -1)
c346.SetCoefficient(D1, 1)

c347 = solver.Constraint(0, infinity, "c347")
c347.SetCoefficient(set7, -1)
c347.SetCoefficient(R1, 1)

c35 = solver.Constraint(0, infinity, "c35")
c35.SetCoefficient(set8, -1)
c35.SetCoefficient(T2, 1)

c351 = solver.Constraint(0, infinity, "c351")
c351.SetCoefficient(set8, -1)
c351.SetCoefficient(W1, 1)

c352 = solver.Constraint(0, infinity, "c352")
c352.SetCoefficient(set8, -1)
c352.SetCoefficient(L3, 1)

c353 = solver.Constraint(0, infinity, "c353")
c353.SetCoefficient(set8, -1)
c353.SetCoefficient(C1, 1)

c354 = solver.Constraint(0, infinity, "c354")
c354.SetCoefficient(set8, -1)
c354.SetCoefficient(O1, 1)

c355 = solver.Constraint(0, infinity, "c355")
c355.SetCoefficient(set8, -1)
c355.SetCoefficient(S3, 1)

c356 = solver.Constraint(0, infinity, "c356")
c356.SetCoefficient(set8, -1)
c356.SetCoefficient(D2, 1)

c357 = solver.Constraint(0, infinity, "c357")
c357.SetCoefficient(set8, -1)
c357.SetCoefficient(R4, 1)

c36 = solver.Constraint(0, infinity, "c36")
c36.SetCoefficient(set9, -1)
c36.SetCoefficient(T2, 1)

c361 = solver.Constraint(0, infinity, "c361")
c361.SetCoefficient(set9, -1)
c361.SetCoefficient(W1, 1)

c362 = solver.Constraint(0, infinity, "c362")
c362.SetCoefficient(set9, -1)
c362.SetCoefficient(L2, 1)

c363 = solver.Constraint(0, infinity, "c363")
c363.SetCoefficient(set9, -1)
c363.SetCoefficient(C3, 1)

c364 = solver.Constraint(0, infinity, "c364")
c364.SetCoefficient(set9, -1)
c364.SetCoefficient(O2, 1)

c365 = solver.Constraint(0, infinity, "c365")
c365.SetCoefficient(set9, -1)
c365.SetCoefficient(S2, 1)

c366 = solver.Constraint(0, infinity, "c366")
c366.SetCoefficient(set9, -1)
c366.SetCoefficient(D2, 1)

c367 = solver.Constraint(0, infinity, "c367")
c367.SetCoefficient(set9, -1)
c367.SetCoefficient(R2, 1)

c37 = solver.Constraint(0, infinity, "c37")
c37.SetCoefficient(set10, -1)
c37.SetCoefficient(T1, 1)

c371 = solver.Constraint(0, infinity, "c371")
c371.SetCoefficient(set10, -1)
c371.SetCoefficient(W1, 1)

c372 = solver.Constraint(0, infinity, "c372")
c372.SetCoefficient(set10, -1)
c372.SetCoefficient(L1, 1)

c373 = solver.Constraint(0, infinity, "c373")
c373.SetCoefficient(set10, -1)
c373.SetCoefficient(C1, 1)

c374 = solver.Constraint(0, infinity, "c374")
c374.SetCoefficient(set10, -1)
c374.SetCoefficient(O3, 1)

c375 = solver.Constraint(0, infinity, "c375")
c375.SetCoefficient(set10, -1)
c375.SetCoefficient(S4, 1)

c376 = solver.Constraint(0, infinity, "c376")
c376.SetCoefficient(set10, -1)
c376.SetCoefficient(D1, 1)

c377 = solver.Constraint(0, infinity, "c377")
c377.SetCoefficient(set10, -1)
c377.SetCoefficient(R3, 1)

c38 = solver.Constraint(0, infinity, "c38")
c38.SetCoefficient(set11, -1)
c38.SetCoefficient(T3, 1)

c381 = solver.Constraint(0, infinity, "c381")
c381.SetCoefficient(set11, -1)
c381.SetCoefficient(W1, 1)

c382 = solver.Constraint(0, infinity, "c382")
c382.SetCoefficient(set11, -1)
c382.SetCoefficient(L3, 1)

c383 = solver.Constraint(0, infinity, "c383")
c383.SetCoefficient(set11, -1)
c383.SetCoefficient(C3, 1)

c384 = solver.Constraint(0, infinity, "c384")
c384.SetCoefficient(set11, -1)
c384.SetCoefficient(O1, 1)

c385 = solver.Constraint(0, infinity, "c385")
c385.SetCoefficient(set11, -1)
c385.SetCoefficient(S1, 1)

c386 = solver.Constraint(0, infinity, "c386")
c386.SetCoefficient(set11, -1)
c386.SetCoefficient(D1, 1)

c387 = solver.Constraint(0, infinity, "c387")
c387.SetCoefficient(set11, -1)
c387.SetCoefficient(R3, 1)

c39 = solver.Constraint(0, infinity, "c39")
c39.SetCoefficient(set12, -1)
c39.SetCoefficient(T2, 1)

c391 = solver.Constraint(0, infinity, "c391")
c391.SetCoefficient(set12, -1)
c391.SetCoefficient(W2, 1)

c392 = solver.Constraint(0, infinity, "c392")
c392.SetCoefficient(set12, -1)
c392.SetCoefficient(L1, 1)

c393 = solver.Constraint(0, infinity, "c393")
c393.SetCoefficient(set12, -1)
c393.SetCoefficient(C2, 1)

c394 = solver.Constraint(0, infinity, "c394")
c394.SetCoefficient(set12, -1)
c394.SetCoefficient(O2, 1)

c395 = solver.Constraint(0, infinity, "c395")
c395.SetCoefficient(set12, -1)
c395.SetCoefficient(S4, 1)

c396 = solver.Constraint(0, infinity, "c396")
c396.SetCoefficient(set12, -1)
c396.SetCoefficient(D2, 1)

c397 = solver.Constraint(0, infinity, "c397")
c397.SetCoefficient(set12, -1)
c397.SetCoefficient(R2, 1)

c40 = solver.Constraint(0, infinity, "c40")
c40.SetCoefficient(set13, -1)
c40.SetCoefficient(T4, 1)

c401 = solver.Constraint(0, infinity, "c401")
c401.SetCoefficient(set13, -1)
c401.SetCoefficient(W4, 1)

c402 = solver.Constraint(0, infinity, "c402")
c402.SetCoefficient(set13, -1)
c402.SetCoefficient(L3, 1)

c403 = solver.Constraint(0, infinity, "c403")
c403.SetCoefficient(set13, -1)
c403.SetCoefficient(C3, 1)

c404 = solver.Constraint(0, infinity, "c404")
c404.SetCoefficient(set13, -1)
c404.SetCoefficient(O1, 1)

c405 = solver.Constraint(0, infinity, "c405")
c405.SetCoefficient(set13, -1)
c405.SetCoefficient(S2, 1)

c406 = solver.Constraint(0, infinity, "c406")
c406.SetCoefficient(set13, -1)
c406.SetCoefficient(D1, 1)

c407 = solver.Constraint(0, infinity, "c407")
c407.SetCoefficient(set13, -1)
c407.SetCoefficient(R3, 1)

c41 = solver.Constraint(0, infinity,"c41")
c41.SetCoefficient(set14, -1)
c41.SetCoefficient(T4, 1)

c411 = solver.Constraint(0, infinity,"c411")
c411.SetCoefficient(set14, -1)
c411.SetCoefficient(W4, 1)

c412 = solver.Constraint(0, infinity,"c412")
c412.SetCoefficient(set14, -1)
c412.SetCoefficient(L4, 1)

c413 = solver.Constraint(0, infinity,"c413")
c413.SetCoefficient(set14, -1)
c413.SetCoefficient(C1, 1)

c414 = solver.Constraint(0, infinity,"c414")
c414.SetCoefficient(set14, -1)
c414.SetCoefficient(O3, 1)

c415 = solver.Constraint(0, infinity,"c415")
c415.SetCoefficient(set14, -1)
c415.SetCoefficient(S1, 1)

c416 = solver.Constraint(0, infinity,"c416")
c416.SetCoefficient(set14, -1)
c416.SetCoefficient(R1, 1)

c42 = solver.Constraint(0, infinity,"c42")
c42.SetCoefficient(set15, -1)
c42.SetCoefficient(T3, 1)

c421 = solver.Constraint(0, infinity,"c421")
c421.SetCoefficient(set15, -1)
c421.SetCoefficient(W3, 1)

c422 = solver.Constraint(0, infinity,"c422")
c422.SetCoefficient(set15, -1)
c422.SetCoefficient(L1, 1)

c423 = solver.Constraint(0, infinity,"c423")
c423.SetCoefficient(set15, -1)
c423.SetCoefficient(C1, 1)

c424 = solver.Constraint(0, infinity,"c424")
c424.SetCoefficient(set15, -1)
c424.SetCoefficient(O1, 1)

c425 = solver.Constraint(0, infinity,"c425")
c425.SetCoefficient(set15, -1)
c425.SetCoefficient(S3, 1)

c426 = solver.Constraint(0, infinity,"c426")
c426.SetCoefficient(set15, -1)
c426.SetCoefficient(R3, 1)

c43 = solver.Constraint(0, infinity,"c43")
c43.SetCoefficient(set16, -1)
c43.SetCoefficient(T3, 1)

c431 = solver.Constraint(0, infinity,"c431")
c431.SetCoefficient(set16, -1)
c431.SetCoefficient(W3, 1)

c432 = solver.Constraint(0, infinity,"c432")
c432.SetCoefficient(set16, -1)
c432.SetCoefficient(L4, 1)

c433 = solver.Constraint(0, infinity,"c433")
c433.SetCoefficient(set16, -1)
c433.SetCoefficient(C1, 1)

c434 = solver.Constraint(0, infinity,"c434")
c434.SetCoefficient(set16, -1)
c434.SetCoefficient(O3, 1)

c435 = solver.Constraint(0, infinity,"c435")
c435.SetCoefficient(set16, -1)
c435.SetCoefficient(S2, 1)

c436 = solver.Constraint(0, infinity,"c436")
c436.SetCoefficient(set16, -1)
c436.SetCoefficient(R1, 1)

c44 = solver.Constraint(0, infinity,"c44")
c44.SetCoefficient(set17, -1)
c44.SetCoefficient(T1, 1)

c441 = solver.Constraint(0, infinity,"c441")
c441.SetCoefficient(set17, -1)
c441.SetCoefficient(W4, 1)

c442 = solver.Constraint(0, infinity,"c442")
c442.SetCoefficient(set17, -1)
c442.SetCoefficient(L2, 1)

c443 = solver.Constraint(0, infinity,"c443")
c443.SetCoefficient(set17, -1)
c443.SetCoefficient(C3, 1)

c444 = solver.Constraint(0, infinity,"c444")
c444.SetCoefficient(set17, -1)
c444.SetCoefficient(O3, 1)

c445 = solver.Constraint(0, infinity,"c445")
c445.SetCoefficient(set17, -1)
c445.SetCoefficient(S4, 1)

c446 = solver.Constraint(0, infinity,"c446")
c446.SetCoefficient(set17, -1)
c446.SetCoefficient(R3, 1)

c45 = solver.Constraint(0, infinity,"c45")
c45.SetCoefficient(set18, -1)
c45.SetCoefficient(T2, 1)

c451 = solver.Constraint(0, infinity,"c451")
c451.SetCoefficient(set18, -1)
c451.SetCoefficient(W3, 1)

c452 = solver.Constraint(0, infinity,"c452")
c452.SetCoefficient(set18, -1)
c452.SetCoefficient(L3, 1)

c453 = solver.Constraint(0, infinity,"c453")
c453.SetCoefficient(set18, -1)
c453.SetCoefficient(C2, 1)

c454 = solver.Constraint(0, infinity,"c454")
c454.SetCoefficient(set18, -1)
c454.SetCoefficient(O4, 1)

c455 = solver.Constraint(0, infinity,"c455")
c455.SetCoefficient(set18, -1)
c455.SetCoefficient(S1, 1)

c456 = solver.Constraint(0, infinity,"c456")
c456.SetCoefficient(set18, -1)
c456.SetCoefficient(R2, 1)

c46 = solver.Constraint(0, infinity,"c46")
c46.SetCoefficient(set19, -1)
c46.SetCoefficient(T2, 1)

c461 = solver.Constraint(0, infinity,"c461")
c461.SetCoefficient(set19, -1)
c461.SetCoefficient(W4, 1)

c462 = solver.Constraint(0, infinity,"c462")
c462.SetCoefficient(set19, -1)
c462.SetCoefficient(L4, 1)

c463 = solver.Constraint(0, infinity,"c463")
c463.SetCoefficient(set19, -1)
c463.SetCoefficient(C4, 1)

c464 = solver.Constraint(0, infinity,"c464")
c464.SetCoefficient(set19, -1)
c464.SetCoefficient(O4, 1)

c465 = solver.Constraint(0, infinity,"c465")
c465.SetCoefficient(set19, -1)
c465.SetCoefficient(S2, 1)

c466 = solver.Constraint(0, infinity,"c466")
c466.SetCoefficient(set19, -1)
c466.SetCoefficient(R4, 1)

c47 = solver.Constraint(0, infinity,"c47")
c47.SetCoefficient(set20, -1)
c47.SetCoefficient(T2, 1)

c471 = solver.Constraint(0, infinity,"c471")
c471.SetCoefficient(set20, -1)
c471.SetCoefficient(W3, 1)

c472 = solver.Constraint(0, infinity,"c472")
c472.SetCoefficient(set20, -1)
c472.SetCoefficient(L1, 1)

c473 = solver.Constraint(0, infinity,"c473")
c473.SetCoefficient(set20, -1)
c473.SetCoefficient(C1, 1)

c474 = solver.Constraint(0, infinity,"c474")
c474.SetCoefficient(set20, -1)
c474.SetCoefficient(O2, 1)

c475 = solver.Constraint(0, infinity,"c475")
c475.SetCoefficient(set20, -1)
c475.SetCoefficient(S3, 1)

c476 = solver.Constraint(0, infinity,"c476")
c476.SetCoefficient(set20, -1)
c476.SetCoefficient(R4, 1)


# ==========================================================
# Objective Function
# ==========================================================

objective = solver.Objective()

objective.SetCoefficient(set1, 1)
objective.SetCoefficient(set2, 1)
objective.SetCoefficient(set3, 1)
objective.SetCoefficient(set4, 1)
objective.SetCoefficient(set5, 1)
objective.SetCoefficient(set6, 1)
objective.SetCoefficient(set7, 1)
objective.SetCoefficient(set8, 1)
objective.SetCoefficient(set9, 1)
objective.SetCoefficient(set10, 1)
objective.SetCoefficient(set11, 1)
objective.SetCoefficient(set12, 1)
objective.SetCoefficient(set13, 1)
objective.SetCoefficient(set14, 1)
objective.SetCoefficient(set15, 1)
objective.SetCoefficient(set16, 1)
objective.SetCoefficient(set17, 1)
objective.SetCoefficient(set18, 1)
objective.SetCoefficient(set19, 1)
objective.SetCoefficient(set20, 1)





objective.SetMaximization()

# ==========================================================
# Solve
# ==========================================================

status = solver.Solve()

# ==========================================================
# Solution
# ==========================================================

if status == pywraplp.Solver.OPTIMAL:

    print("\n========== Optimal Solution ==========")

    print(f"Optimal objective value = {objective.Value():.2f}\n")

    print("Decision Variables")
    for var in solver.variables():
        print(
            f"{var.name():3} = {var.solution_value():6.2f}"
            #f"    Reduced Cost = {var.ReducedCost():7.2f}"
        )

    print("\nConstraints")
    for c in solver.constraints():

        activity = sum(
            c.GetCoefficient(v) * v.solution_value()
            for v in solver.variables()
        )

        print(
            f"{c.name():10}"
            f" Activity = {activity:6.2f}"
            f" RHS = {c.ub():6.2f}"
            #f" Shadow Price = {c.DualValue():7.2f}"
        )

    print("\nStatistics")
    print(f"Variables   : {solver.NumVariables()}")
    print(f"Constraints : {solver.NumConstraints()}")
    print(f"Iterations  : {solver.iterations()}")
    print(f"Wall Time   : {solver.wall_time()} ms")

else:
    print("No optimal solution found.")