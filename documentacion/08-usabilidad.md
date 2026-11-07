# MediCore · Prueba de usabilidad

**Estado: pendiente de ejecutar.** El documento del proyecto exige al menos 5 participantes reales. Este archivo define la prueba; los resultados se llenan al aplicarla y no deben inventarse.

## Preparación

- Ambiente: QA local (`scripts/qa.sh up`, `scripts/qa.sh seed`), web en `http://127.0.0.1:8080` y app móvil en emulador o teléfono.
- Participantes: mínimo 5 personas que no hayan desarrollado el sistema. Al menos 1 por cada perfil: recepción, médico, laboratorio y paciente.
- Moderación: una persona guía y otra anota. No se ayuda salvo que el participante quede bloqueado; esa tarea cuenta como no completada.
- Datos: solo cuentas ficticias. No se registra información personal de los participantes.

## Tareas

| ID | Perfil | Tarea | Éxito | Tiempo objetivo |
|---|---|---|---|---|
| U1 | Recepción | Registrar un paciente nuevo | Aparece en la lista | 2 min |
| U2 | Recepción | Reservar una cita para ese paciente | Aparece en la agenda | 2 min |
| U3 | Recepción | Cancelar esa cita | Queda cancelada y el horario se libera | 1 min |
| U4 | Médico | Abrir la agenda y el expediente de un paciente | Ve consultas y recetas | 1 min |
| U5 | Médico | Registrar una consulta con una receta de dos medicamentos | La receta aparece en el expediente | 4 min |
| U6 | Laboratorio | Registrar un resultado con dos parámetros | Aparece con su estado de notificación | 3 min |
| U7 | Paciente | Reservar una cita en la app | Aparece en "Mis citas" | 2 min |
| U8 | Paciente | Consultar un resultado y una notificación | Abre el detalle | 2 min |

## Registro por participante

| Participante (código) | Perfil | Tarea | Completó (sí/no) | Tiempo | Errores | Ayuda pedida | Comentario |
|---|---|---|---|---|---|---|---|
| P1 | | | | | | | |
| P2 | | | | | | | |
| P3 | | | | | | | |
| P4 | | | | | | | |
| P5 | | | | | | | |

## Cuestionario SUS

Al terminar, cada participante responde de 1 (muy en desacuerdo) a 5 (muy de acuerdo):

1. Creo que me gustaría usar este sistema con frecuencia.
2. Encontré el sistema innecesariamente complejo.
3. Pensé que el sistema era fácil de usar.
4. Creo que necesitaría ayuda técnica para poder usarlo.
5. Las funciones del sistema estaban bien integradas.
6. Pensé que había demasiada inconsistencia en el sistema.
7. Imagino que la mayoría aprendería a usarlo rápidamente.
8. Encontré el sistema muy incómodo de usar.
9. Me sentí seguro usando el sistema.
10. Necesité aprender muchas cosas antes de poder usarlo.

**Cálculo:** a las preguntas impares se les resta 1; las pares se restan de 5. La suma se multiplica por 2,5.

## Criterios de aceptación

- Al menos 80 % de las tareas completadas sin ayuda.
- SUS promedio de 68 o más.
- Ningún problema que impida completar U2, U5 o U6.

## Resultados

Por llenar después de la sesión: tasa de finalización, tiempo promedio, SUS promedio, problemas encontrados y su severidad. Cada problema se registra como Issue.
