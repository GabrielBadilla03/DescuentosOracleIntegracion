// ~/js/CreateDeclaraciones.js
// Versión corregida para que no haya incongruencias Controller ↔ Vista ↔ JS
// - No usa " " (espacio) como vacío: normaliza a ""
// - Para PROMOCIONAL: carga artículos por endpoint BuscarArticulosPorLinea (filtra por XXORA END_DATE NULL)
// - Mantiene filtro por medida (en promo se filtra por medida usando cache local como "lookup")
// - Corrige desArticulo al editar
// - Soporta copia inicial si la vista define window.detallesCopia = [...] (recomendado)

let detalles = [];
let esCopia = false;                // usado por el modal Copiar Descuentos (UI)
let filtroClienteTimeout = null;
let filtroArticuloTimeout = null;
let bloqueoTraerDescuentos = false; // usado para copia inicial desde solicitud (querystring)


function appUrl(path) {
    const base = (window.AppBasePath || '/').replace(/\/$/, '');
    const cleanPath = String(path || '').replace(/^\//, '');
    return `${base}/${cleanPath}`;
}

$(document).ready(function () {

    // =========================================================
    //  UTILIDADES GENERALES
    // =========================================================
    const articulosAll = Array.isArray(window.__articulosAll) ? window.__articulosAll : [];

    // Map rápido codArticulo -> { medida, codLinea, desArticulo }
    const articuloByCod = new Map(
        articulosAll
            .filter(a => a && a.codArticulo)
            .map(a => [String(a.codArticulo).trim(), {
                medida: (a.medida ?? '').toString().trim(),
                codLinea: (a.codLinea ?? '').toString().trim(),
                desLinea: (a.desLinea ?? '').toString().trim(),   // ✅
                desArticulo: (a.desArticulo ?? '').toString().trim()
            }])
    );

    function safeTrim(v) {
        return (v === undefined || v === null) ? '' : String(v).trim();
    }

    function normalizeEmpty(v) {
        // Convierte null/undefined/"   " => ""
        const t = safeTrim(v);
        return t.length ? t : '';
    }

    function escAttr(s) {
        return String(s ?? '').replace(/"/g, '&quot;');
    }

    function setSelectPlaceholder($select, text, { disabled = true, selected = true } = {}) {
        $select.empty().append($('<option>', {
            value: '',
            text: text || '-- Seleccione una opción --',
            disabled: disabled,
            selected: selected
        }));
        $select.prop('disabled', disabled);
    }

    function setSelectLoading($select, text = 'Buscando...') {
        setSelectPlaceholder($select, text, { disabled: true, selected: true });
    }

    // =========================================================
    //  MODALES (BS5/BS4)
    // =========================================================
    function showModal(id) {
        const el = document.getElementById(id);
        if (!el) {
            console.error("No se encontró el modal con id:", id);
            return;
        }

        if (window.bootstrap && bootstrap.Modal) {
            const modal = bootstrap.Modal.getOrCreateInstance(el);
            modal.show();
            return;
        }

        if (window.$ && $(el).modal) {
            $(el).modal('show');
            return;
        }

        console.error("No se encontró ni bootstrap.Modal ni $(...).modal. Revisa Bootstrap JS.");
    }

    function hideModal(id) {
        const el = document.getElementById(id);
        if (!el) return;

        if (window.bootstrap && bootstrap.Modal) {
            const modal = bootstrap.Modal.getInstance(el);
            if (modal) modal.hide();
            return;
        }

        if (window.$ && $(el).modal) {
            $(el).modal('hide');
            return;
        }
    }

    // =========================================================
    //  ESTADO GENERAL / VALIDACIONES
    // =========================================================
    function validarBotonCrear() {
        const codCliente = $("#CodCliente").val();
        $("#btnCrear").prop("disabled", !codCliente);

        actualizarBotonesAccion();
    }

    function actualizarEstadoFechas() {
        const tipo = $("#Tipodescuento").val();

        if (tipo === "Descuento Fijo") {
            // Nota: disabled => no postea. Si tu server maneja esto bien (recomendado), ok.
            $("#Fechainicio").prop("disabled", true).val('');
            $("#Fechafin").prop("disabled", true).val('');
        } else if (tipo === "Descuento Promocional") {
            $("#Fechainicio").prop("disabled", false);
            $("#Fechafin").prop("disabled", false);
        } else {
            $("#Fechainicio").prop("disabled", true).val('');
            $("#Fechafin").prop("disabled", true).val('');
        }
    }

    function esPromocional() {
        const v = (safeTrim($('#Tipodescuento').val()) || '').toLowerCase();
        return v.includes('promocional') || v === 'promocional';
    }

    function getCodCliente() {
        return safeTrim($('#CodCliente').val());
    }

    function getBuNombre() {
        return safeTrim($('#CodCia').val() || 'LANCO_CR');
    }


    // Línea y Clase usan el mismo contexto de elegibilidad que Artículo.
    function paramsCatalogoElegible(filtro) {
        return {
            filtro: filtro || '',
            codCliente: getCodCliente(),
            buNombre: getBuNombre(),
            tipoDescuento: $('#Tipodescuento').val() || ''
        };
    }

    function paramsClaseElegible(codLinea, filtro) {
        return {
            ...paramsCatalogoElegible(filtro),
            codLinea: codLinea || ''
        };
    }

    // =========================================================
    //  TABLA DETALLES (render + reindex)
    // =========================================================
    function sanitizeDetalle(d) {
        // Normaliza el shape y elimina el " " como vacío
        const codLinea = normalizeEmpty(d?.codLinea);
        const desLinea = normalizeEmpty(d?.desLinea);

        const codArticulo = normalizeEmpty(d?.codArticulo);
        const desArticulo = normalizeEmpty(d?.desArticulo);

        const claseart = normalizeEmpty(d?.claseart);
        const tipo = normalizeEmpty(d?.tipo) || 'P';

        // Valor puede venir como number/string
        let valor = d?.valor;
        if (valor === null || valor === undefined || valor === '') valor = 0;
        const valorNum = Number(valor);
        valor = Number.isFinite(valorNum) ? valorNum : 0;

        return {
            Consecutivodetalle: d?.Consecutivodetalle || d?.consecutivodetalle || 0,
            codLinea,
            desLinea,
            codArticulo,
            desArticulo,
            claseart,
            tipo,
            valor
        };
    }

    function refrescarTablaDetalles(arr) {
        if (!Array.isArray(arr)) return;

        // Sanitizar siempre antes de pintar
        detalles = arr.map(sanitizeDetalle);

        let tablaHtml = '';
        detalles.forEach((item, i) => {
            const codLinea = item.codLinea;
            const desLinea = item.desLinea || '';
            const codArticulo = item.codArticulo || '';
            const tipo = item.tipo;
            const valor = item.valor;
            const claseart = item.claseart || '';

            tablaHtml += `
                <tr
                    data-codarticulo="${escAttr(codArticulo)}"
                    data-codlinea="${escAttr(codLinea)}"
                    data-claseart="${escAttr(claseart)}">
                    <td>
                        <input type="hidden" name="PREDETDESCUENTOs[${i}].COD_LINEA" value="${escAttr(codLinea)}" />
                        ${escAttr(codLinea)}${desLinea ? ' - ' + escAttr(desLinea) : ''}
                    </td>
                    <td>
                        <input type="hidden" name="PREDETDESCUENTOs[${i}].COD_ARTICULO" value="${escAttr(codArticulo)}" />
                        ${escAttr(codArticulo)}
                    </td>
                    <td>
                        <input type="hidden" name="PREDETDESCUENTOs[${i}].TIPO" value="${escAttr(tipo)}" />
                        ${tipo === 'P' ? 'Porcentaje' : tipo === 'M' ? 'Monto' : escAttr(tipo || '')}
                    </td>
                    <td>
                        <input type="hidden" name="PREDETDESCUENTOs[${i}].VALOR" value="${escAttr(valor)}" />
                        ${escAttr(valor)}
                    </td>
                    <td>
                        <input type="hidden" name="PREDETDESCUENTOs[${i}].COD_CLASE" value="${escAttr(claseart)}" />
                        ${escAttr(claseart)}
                    </td>
                    <td>
                        <button type="button" class="btn btn-danger btn-sm btn-editar-detalle" data-index="${i}">
                            Modificar
                        </button>
                    </td>
                </tr>`;
        });

        $('#tabla-detalles tbody').html(tablaHtml);
        reindexDetalles();
        actualizarBotonesPorDetalles();
    }

    function reindexDetalles() {
        $("#tabla-detalles tbody tr").each(function (i, row) {
            $(row).find("input").each(function () {
                const name = $(this).attr("name");
                if (name) {
                    const newName = name.replace(/\[\d+\]/, `[${i}]`);
                    $(this).attr("name", newName);
                }
            });
            $(row).find(".btn-editar-detalle").attr("data-index", i);
        });
    }

    function estadoEsEditable() {
        const e = safeTrim($("#Estado").val()).toLowerCase();
        // Igual que en Edit: solo estos estados permiten modificar
        return (e === "pendiente" || e === "pendiente aprobacion" || e === "pendiente aprobación");
    }

    function actualizarBotonesAccion() {
        const tieneCliente = !!safeTrim($("#CodCliente").val());
        const editable = estadoEsEditable();

        // Copiar: solo si hay cliente seleccionado y estado editable (igual que Edit)
        $("#btnCopiarDescuentos").prop("disabled", !(tieneCliente && editable));

        // Traer: igual, pero además respeta el bloqueo por copia inicial
        const traerDisabled = !(tieneCliente && editable) || bloqueoTraerDescuentos;
        $("#btnTraerDescuentos").prop("disabled", traerDisabled);

        if (bloqueoTraerDescuentos) {
            $("#btnTraerDescuentos").attr("title", "Esta solicitud tiene descuentos copiados inicialmente. Limpie la lista para poder traer descuentos del cliente.");
        } else {
            $("#btnTraerDescuentos").removeAttr("title");
        }
    }

    function actualizarBotonesPorDetalles() {
        const hayDetalles = Array.isArray(detalles) && detalles.length > 0;

        // Si ya hay al menos 1 detalle, se bloquean ambos
        $("#btnTraerDescuentos").prop("disabled", hayDetalles);
        $("#btnCopiarDescuentos").prop("disabled", hayDetalles);
    }

    // =========================================================
    //  CLIENTES (input -> select CodCliente)
    // =========================================================
    $("#filtroCliente").on("input", function () {
        clearTimeout(filtroClienteTimeout);

        filtroClienteTimeout = setTimeout(function () {
            const filtro = $("#filtroCliente").val().trim();
            const $selectCliente = $("#CodCliente");

            if (filtro.length < 2) {
                setSelectPlaceholder($selectCliente, '-- Escriba para buscar --', { disabled: true });
                // No borro detalles automáticamente (evita perder trabajo); si querés, habilitalo:
                // detalles = [];
                // $("#tabla-detalles tbody").empty();
                validarBotonCrear();
                return;
            }

            setSelectLoading($selectCliente, 'Buscando clientes...');

            $.getJSON(appUrl('Predescuentos/BuscarClientes'), { filtro })
                .done(function (data) {
                    $selectCliente.empty();

                    if (!Array.isArray(data) || data.length === 0) {
                        setSelectPlaceholder($selectCliente, '-- Sin resultados --', { disabled: true });
                        validarBotonCrear();
                        return;
                    }

                    setSelectPlaceholder($selectCliente, '-- Seleccione un cliente --', { disabled: false });

                    data.forEach(cliente => {
                        const cod = normalizeEmpty(cliente.codCliente);
                        const nom = normalizeEmpty(cliente.nomCliente);
                        const lugar = normalizeEmpty(cliente.lugar);

                        $selectCliente.append($('<option>', {
                            value: cod,
                            text: cod + ' - ' + nom + (lugar ? ' - ' + lugar : '')
                        }));
                    });

                    $selectCliente.prop("disabled", false);
                    validarBotonCrear();
                })
                .fail(function () {
                    setSelectPlaceholder($selectCliente, '-- Error buscando --', { disabled: true });
                    validarBotonCrear();
                });
        }, 600);
    });

    // =========================================================
    //  TRAER DESCUENTOS DEL CLIENTE (PREDESCLASEORACLE)
    // =========================================================
    $("#btnTraerDescuentos").on("click", function () {
        if (esCopia) return;

        if (bloqueoTraerDescuentos) {
            alert("Esta solicitud tiene descuentos copiados inicialmente. Si querés traer descuentos del cliente, primero limpiá la lista.");
            return;
        }

        const codCliente = $("#CodCliente").val();
        if (!codCliente) {
            alert("Debe seleccionar un cliente primero.");
            return;
        }

        $.getJSON(appUrl('Predescuentos/GetDescuentosCliente'), {
            codCliente: codCliente,
            codCia: "",
            tipoDescuento: $('#Tipodescuento').val() || ''
        })
            .done(function (data) {
                const arr = Array.isArray(data) ? data : [];
                refrescarTablaDetalles(arr);
            })
            .fail(function (xhr) {
                console.error("GetDescuentosCliente error:",
                    "status=", xhr.status,
                    "response=", xhr.responseText);

                alert("Error al traer los descuentos del cliente. Revisá la consola (F12) para ver el detalle.");
            });
    });


    // =========================================================
    //  MODAL AGREGAR MANUAL (tipo/valor + checklists)
    // =========================================================
    function obtenerTipoValorManual() {
        const tipo = $("#manualTipo").val();
        const valorRaw = $("#manualValor").val();
        const valor = parseFloat(valorRaw);

        if (!tipo) {
            alert("Seleccione el tipo de descuento (Porcentaje o Monto).");
            return null;
        }
        if (valorRaw === "" || isNaN(valor)) {
            alert("Ingrese un valor numérico válido para el descuento.");
            return null;
        }
        if (valor < 0) {
            alert("⚠️ El valor del descuento no puede ser negativo.");
            $("#manualValor").focus();
            return null;
        }

        return { tipo, valor };
    }

    function limpiarChecksModalManual() {
        $("#chkLineaTodo, #chkArticuloTodo, #chkClaseTodo").prop("checked", false);

        $("#tbodyLineasChecklist .chk-linea").prop("checked", false);
        $("#tbodyArticulosChecklist .chk-articulo").prop("checked", false);
        $("#tbodyClasesChecklist .chk-clase").prop("checked", false);
    }

    function refrescarChecklistVisible() {
        limpiarChecksModalManual();

        if ($("#seccionLineaChecklist").is(":visible")) {
            cargarLineasChecklist();
            return;
        }

        if ($("#seccionArticuloChecklist").is(":visible")) {
            renderArticulosChecklist();
            return;
        }

        if ($("#seccionClaseChecklist").is(":visible")) {
            const codLinea = ($("#CodLineaClase").val() || "").trim();

            if (codLinea) {
                $("#CodLineaClase").trigger("change");
            } else {
                $("#tbodyClasesChecklist").html(
                    '<tr><td colspan="3" class="text-center text-muted">Seleccione una línea para comenzar.</td></tr>'
                );
            }
        }
    }

    // ---------- Checklist por Línea ----------
    function cargarLineasChecklist() {
        const $tbody = $("#tbodyLineasChecklist");
        $tbody.empty();
        $("#chkLineaTodo").prop("checked", false);

        $.getJSON(appUrl('Predescuentos/BuscarLineas'), paramsCatalogoElegible(""))
            .done(function (data) {
                if (!Array.isArray(data) || data.length === 0) {
                    $tbody.append('<tr><td colspan="3" class="text-center text-muted">Sin resultados</td></tr>');
                    return;
                }

                data.forEach(item => {
                    const codLinea = normalizeEmpty(item.codLinea);
                    const desLinea = normalizeEmpty(item.desLinea);

                    const yaExiste = detalles.some(d =>
                        safeTrim(d.codLinea) === codLinea &&
                        safeTrim(d.claseart) === "" &&
                        safeTrim(d.codArticulo) === ""
                    );

                    const disabledAttr = yaExiste ? 'disabled' : '';
                    const title = yaExiste ? 'Ya existe un descuento para esta línea.' : '';

                    $tbody.append(`
                        <tr>
                            <td>
                                <input type="checkbox"
                                       class="chk-linea"
                                       data-codlinea="${escAttr(codLinea)}"
                                       data-deslinea="${escAttr(desLinea)}"
                                       ${disabledAttr}
                                       title="${escAttr(title)}">
                            </td>
                            <td>${escAttr(codLinea)}</td>
                            <td>${escAttr(desLinea)}</td>
                        </tr>
                    `);
                });
            })
            .fail(function () {
                $tbody.append('<tr><td colspan="3" class="text-center text-danger">Error cargando líneas</td></tr>');
            });
    }

    $("#chkLineaTodo").on("change", function () {
        const checked = $(this).is(":checked");
        $("#tbodyLineasChecklist .chk-linea:not(:disabled)").prop("checked", checked);
    });

    $("#btnAgregarLineasChecklist").on("click", function () {
        const tv = obtenerTipoValorManual();
        if (!tv) return;

        const seleccionados = $("#tbodyLineasChecklist .chk-linea:checked");
        if (seleccionados.length === 0) {
            alert("Seleccione al menos una línea.");
            return;
        }

        let agregados = 0;

        seleccionados.each(function () {
            const codLinea = normalizeEmpty($(this).data("codlinea"));
            const desLinea = normalizeEmpty($(this).data("deslinea"));

            const yaExiste = detalles.some(d =>
                safeTrim(d.codLinea) === codLinea &&
                safeTrim(d.claseart) === "" &&
                safeTrim(d.codArticulo) === ""
            );
            if (yaExiste) return;

            detalles.push({
                codLinea,
                desLinea,
                codArticulo: "",
                desArticulo: "",
                claseart: "",
                tipo: tv.tipo,
                valor: tv.valor
            });

            agregados++;
        });

        refrescarTablaDetalles(detalles);
        refrescarChecklistVisible();

        if (agregados === 0) {
            alert("Las líneas seleccionadas ya estaban agregadas.");
        }
    });

    // =========================================================
    //  CHECKLIST POR ARTÍCULO
    //  - FIJO: cache articulosAll
    //  - PROMO: endpoint BuscarArticulosPorLinea (filtra XXORA END_DATE NULL)
    // =========================================================
    function getDesLineaDesdeSelect(codLinea) {
        if (!codLinea) return "";
        const txt = $(`#CodLineaArticulo option[value="${codLinea}"]`).text() || "";
        return txt.includes(" - ") ? txt.split(" - ").slice(1).join(" - ") : txt;
    }

    function renderArticulosChecklistDesdeServer() {
        const codLineaSel = safeTrim($("#CodLineaArticulo").val()); // puede venir ""
        const filtroMedida = safeTrim($("#filtroMedidaArticulo").val());
        const filtroTxt = safeTrim($("#filtroDescArticulo").val()); // texto

        const $tbody = $("#tbodyArticulosChecklist");
        $tbody.empty();
        $("#chkArticuloTodo").prop("checked", false);

        const hayFiltroTexto = filtroTxt.length >= 2;
        const hayLinea = !!codLineaSel;
        const hayMedida = !!filtroMedida;

        // ✅ si no hay nada para filtrar, no consultamos
        if (!hayLinea && !hayMedida && !hayFiltroTexto) {
            $tbody.append('<tr><td colspan="3" class="text-center text-muted">Use Línea, Medida o Descripción para buscar.</td></tr>');
            return;
        }

        const promo = esPromocional();
        const codCliente = getCodCliente();

        // ✅ PROMO exige cliente (porque valida XXORA por cliente+BU)
        if (promo && !codCliente) {
            $tbody.append('<tr><td colspan="3" class="text-center text-muted">Seleccione cliente primero (promocional).</td></tr>');
            return;
        }

        $.getJSON(appUrl('Predescuentos/BuscarArticulosPorLinea'), {
            codLinea: codLineaSel || '',
            filtro: filtroTxt || '',
            medida: filtroMedida || '',
            codCliente: codCliente || '',              // en fijo puede ir vacío
            buNombre: getBuNombre(),
            tipoDescuento: $('#Tipodescuento').val() || ''
        })
            .done(function (data) {
                let arr = Array.isArray(data) ? data : [];
                const MAX = 400;
                if (arr.length > MAX) arr = arr.slice(0, MAX);

                if (arr.length === 0) {
                    $tbody.append('<tr><td colspan="3" class="text-center text-muted">Sin resultados</td></tr>');
                    return;
                }

                arr.forEach(item => {
                    const codArticulo = safeTrim(item.codArticulo);
                    const desArticulo = safeTrim(item.desArticulo);

                    // si el usuario no eligió línea, usamos la línea real del item
                    const codLineaItem = codLineaSel || safeTrim(item.codLinea);
                    const desLineaItem = safeTrim(item.desLinea);

                    const yaExiste = detalles.some(d =>
                        safeTrim(d.codLinea) === codLineaItem &&
                        safeTrim(d.codArticulo) === codArticulo
                    );

                    const disabledAttr = yaExiste ? 'disabled' : '';
                    const title = yaExiste ? 'Ya existe un descuento para este artículo.' : '';

                    $tbody.append(`
                <tr>
                    <td>
                        <input type="checkbox"
                               class="chk-articulo"
                               data-codlinea="${escAttr(codLineaItem)}"
                               data-deslinea="${escAttr(desLineaItem)}"
                               data-codarticulo="${escAttr(codArticulo)}"
                               data-desarticulo="${escAttr(desArticulo)}"
                               ${disabledAttr}
                               title="${escAttr(title)}">
                    </td>
                    <td>${escAttr(codArticulo)}</td>
                    <td>${escAttr(desArticulo)}</td>
                </tr>
            `);
                });
            })
            .fail(function () {
                $tbody.append('<tr><td colspan="3" class="text-center text-danger">Error cargando artículos</td></tr>');
            });
    }

    function renderArticulosChecklist() {
        renderArticulosChecklistDesdeServer();
    }

    // hooks existentes (se quedan igual)
    $("#filtroMedidaArticulo").on("change", renderArticulosChecklist);
    $("#filtroDescArticulo").on("input", function () {
        clearTimeout(filtroArticuloTimeout);
        filtroArticuloTimeout = setTimeout(renderArticulosChecklist, 250);
    });

    let xhrBuscarLineasArticulo = null;

    $("#filtroLineaArticulo").on("keyup", function () {
        const filtro = $(this).val().trim();
        const $selectLinea = $("#CodLineaArticulo");

        $selectLinea.empty()
            .append('<option value="">Seleccione una línea...</option>')
            .prop("disabled", true);

        if (filtro.length < 2) return;

        // ✅ aborta la anterior si aún está viva
        if (xhrBuscarLineasArticulo && xhrBuscarLineasArticulo.readyState !== 4) {
            xhrBuscarLineasArticulo.abort();
        }

        xhrBuscarLineasArticulo = $.getJSON(appUrl('Predescuentos/BuscarLineas'), paramsCatalogoElegible(filtro))
            .done(function (data) {
                if (!Array.isArray(data) || data.length === 0) return;

                data.forEach(item => {
                    $selectLinea.append($('<option>', {
                        value: item.codLinea,
                        text: item.codLinea + ' - ' + item.desLinea
                    }));
                });

                $selectLinea.prop("disabled", false);
            });
    });


    $("#filtroMedidaArticulo").on("change", renderArticulosChecklist);

    $("#filtroDescArticulo").on("input", function () {
        clearTimeout(filtroArticuloTimeout);
        filtroArticuloTimeout = setTimeout(renderArticulosChecklist, 250);
    });


    $("#CodLineaArticulo").on("change", function () {
        $("#filtroDescArticulo").val("");
        renderArticulosChecklist();
    });

    $("#chkArticuloTodo").on("change", function () {
        const checked = $(this).is(":checked");
        $("#tbodyArticulosChecklist .chk-articulo:not(:disabled)").prop("checked", checked);
    });

    $("#btnAgregarArticulosChecklist").on("click", function () {
        const tv = obtenerTipoValorManual();
        if (!tv) return;

        const seleccionados = $("#tbodyArticulosChecklist .chk-articulo:checked");
        if (seleccionados.length === 0) {
            alert("Seleccione al menos un artículo.");
            return;
        }

        let agregados = 0;

        seleccionados.each(function () {
            const codLinea = normalizeEmpty($(this).data("codlinea"));
            const desLinea = normalizeEmpty($(this).data("deslinea"));
            const codArticulo = normalizeEmpty($(this).data("codarticulo"));
            const desArticulo = normalizeEmpty($(this).data("desarticulo"));

            const yaExiste = detalles.some(d =>
                safeTrim(d.codLinea) === codLinea &&
                safeTrim(d.codArticulo) === codArticulo
            );
            if (yaExiste) return;

            detalles.push({
                codLinea,
                desLinea,
                codArticulo,
                desArticulo,
                claseart: "",
                tipo: tv.tipo,
                valor: tv.valor
            });

            agregados++;
        });

        refrescarTablaDetalles(detalles);
        refrescarChecklistVisible();

        if (agregados === 0) {
            alert("Los artículos seleccionados ya estaban agregados.");
        }
    });

    // =========================================================
    //  CHECKLIST POR CLASE
    // =========================================================
    $("#filtroLineaClase").on("keyup", function () {
        const filtro = $(this).val().trim();
        const $selectLinea = $("#CodLineaClase");

        $selectLinea.empty()
            .append('<option value="">Seleccione una línea...</option>')
            .prop("disabled", true);

        $("#tbodyClasesChecklist").empty();
        $("#chkClaseTodo").prop("checked", false);

        if (filtro.length < 2) return;

        $.getJSON(appUrl('Predescuentos/BuscarLineas'), paramsCatalogoElegible(filtro), function (data) {
            if (!Array.isArray(data) || data.length === 0) return;

            data.forEach(item => {
                $selectLinea.append($('<option>', {
                    value: item.codLinea,
                    text: item.codLinea + ' - ' + item.desLinea
                }));
            });

            $selectLinea.prop("disabled", false);
        });
    });

    $("#CodLineaClase").on("change", function () {
        const codLinea = $(this).val();
        const $tbody = $("#tbodyClasesChecklist");

        $tbody.empty();
        $("#chkClaseTodo").prop("checked", false);

        if (!codLinea) return;

        $.getJSON(appUrl('Predescuentos/BuscarClaseartsPorlinea'), paramsClaseElegible(codLinea, ""), function (data) {
            if (!Array.isArray(data) || data.length === 0) {
                $tbody.append('<tr><td colspan="3" class="text-center text-muted">Sin resultados</td></tr>');
                return;
            }

            const textoLinea = $("#CodLineaClase option:selected").text();
            const desLinea = textoLinea.split(" - ").slice(1).join(" - ") || "";

            data.forEach(item => {
                const codClase = normalizeEmpty(item.codigo);
                const desClase = normalizeEmpty(item.descripcion);

                const yaExiste = detalles.some(d =>
                    safeTrim(d.codLinea) === safeTrim(codLinea) &&
                    safeTrim(d.claseart) === safeTrim(codClase) &&
                    safeTrim(d.codArticulo) === ""
                );

                const disabledAttr = yaExiste ? 'disabled' : '';
                const title = yaExiste ? 'Ya existe un descuento para esta clase.' : '';

                $tbody.append(`
                    <tr>
                        <td>
                            <input type="checkbox"
                                   class="chk-clase"
                                   data-codlinea="${escAttr(codLinea)}"
                                   data-deslinea="${escAttr(desLinea)}"
                                   data-codclase="${escAttr(codClase)}"
                                   data-desclase="${escAttr(desClase)}"
                                   ${disabledAttr}
                                   title="${escAttr(title)}">
                        </td>
                        <td>${escAttr(codClase)}</td>
                        <td>${escAttr(desClase)}</td>
                    </tr>
                `);
            });
        });
    });

    $("#chkClaseTodo").on("change", function () {
        const checked = $(this).is(":checked");
        $("#tbodyClasesChecklist .chk-clase:not(:disabled)").prop("checked", checked);
    });

    $("#btnAgregarClasesChecklist").on("click", function () {
        const tv = obtenerTipoValorManual();
        if (!tv) return;

        const seleccionados = $("#tbodyClasesChecklist .chk-clase:checked");
        if (seleccionados.length === 0) {
            alert("Seleccione al menos una clase.");
            return;
        }

        let agregados = 0;

        seleccionados.each(function () {
            const codLinea = normalizeEmpty($(this).data("codlinea"));
            const desLinea = normalizeEmpty($(this).data("deslinea"));
            const codClase = normalizeEmpty($(this).data("codclase"));

            const yaExiste = detalles.some(d =>
                safeTrim(d.codLinea) === codLinea &&
                safeTrim(d.claseart) === codClase &&
                safeTrim(d.codArticulo) === ""
            );
            if (yaExiste) return;

            detalles.push({
                codLinea,
                desLinea,
                codArticulo: "",
                desArticulo: "",
                claseart: codClase,
                tipo: tv.tipo,
                valor: tv.valor
            });

            agregados++;
        });

        refrescarTablaDetalles(detalles);
        refrescarChecklistVisible();

        if (agregados === 0) {
            alert("Las clases seleccionadas ya estaban agregadas.");
        }
    });

    // =========================================================
    //  ABRIR MODAL AGREGAR MANUAL
    // =========================================================
    function abrirModalAgregar(modo) {
        const clienteSeleccionado = $("#CodCliente").val();
        if (!clienteSeleccionado) {
            alert("Debe seleccionar un cliente antes de agregar descuentos.");
            return;
        }

        $("#manualTipo").val("P");
        $("#manualValor").val("");

        $("#chkLineaTodo, #chkArticuloTodo, #chkClaseTodo").prop("checked", false);
        $("#tbodyLineasChecklist").empty();
        $("#tbodyArticulosChecklist").empty();
        $("#tbodyClasesChecklist").empty();

        $("#seccionLineaChecklist, #seccionArticuloChecklist, #seccionClaseChecklist").hide();
        $("#btnAgregarLineasChecklist, #btnAgregarArticulosChecklist, #btnAgregarClasesChecklist").hide();

        if (modo === "linea") {
            $("#seccionLineaChecklist").show();
            $("#btnAgregarLineasChecklist").show();
            $("#modalLabelManual").text("Agregar descuentos por Línea");
            cargarLineasChecklist();
        } else if (modo === "articulo") {
            $("#seccionArticuloChecklist").show();
            $("#btnAgregarArticulosChecklist").show();
            $("#modalLabelManual").text("Agregar descuentos por Artículo");

            $("#filtroLineaArticulo").val("");
            $("#CodLineaArticulo").empty()
                .append('<option value="">Seleccione una línea...</option>')
                .prop("disabled", true);

            $("#filtroMedidaArticulo").val("");
            $("#filtroDescArticulo").val("");
            $("#tbodyArticulosChecklist").html('<tr><td colspan="3" class="text-center text-muted">Seleccione una línea para comenzar.</td></tr>');
        } else if (modo === "clase") {
            $("#seccionClaseChecklist").show();
            $("#btnAgregarClasesChecklist").show();
            $("#modalLabelManual").text("Agregar descuentos por Clase");

            $("#filtroLineaClase").val("");
            $("#CodLineaClase").empty()
                .append('<option value="">Seleccione una línea...</option>')
                .prop("disabled", true);
        }

        showModal('modalAgregarManual');
    }

    $("#btnAgregarLinea").on("click", () => abrirModalAgregar("linea"));
    $("#btnAgregarArticulo").on("click", () => abrirModalAgregar("articulo"));
    $("#btnAgregarClase").on("click", () => abrirModalAgregar("clase"));

    $("#btnCerrarAgregarManual").on("click", function () {
        hideModal('modalAgregarManual');
    });

    $("#btnLimpiarLista").on("click", function () {
        if (!confirm("¿Estás seguro de que deseas borrar todos los elementos de la lista?")) return;

        detalles = [];
        refrescarTablaDetalles(detalles);

        // 🔥 Si estaba bloqueado por copia inicial, acá se libera
        bloqueoTraerDescuentos = false;
        esCopia = false;

        actualizarBotonesAccion();
    });


    actualizarEstadoFechas();
    validarBotonCrear();
    actualizarBotonesAccion();

    if (Array.isArray(window.detallesCopia) && window.detallesCopia.length > 0) {
        bloqueoTraerDescuentos = true;
        refrescarTablaDetalles(window.detallesCopia);
        actualizarBotonesAccion();
    }

    // =========================================================
    //  COPIAR DESCUENTOS (modal + búsquedas)
    // =========================================================
    function llenarSelectConResultados($select, data, valueField, textField) {
        $select.empty();
        $select.append($('<option>', {
            value: '',
            text: '-- Seleccione un cliente --',
            disabled: false,
            selected: true
        }));

        data.forEach(item => {
            $select.append($('<option>', {
                value: item[valueField],
                text: item[textField]
            }));
        });

        $select.trigger("change");
    }

    function buscarClientesPara(selector, filtro) {
        const $select = $(selector);
        const f = (filtro || '').trim();

        if (f.length < 2) {
            $select.empty().prop("disabled", true);
            return;
        }

        $.getJSON(appUrl('Predescuentos/BuscarClientes'), { filtro: f }, function (data) {
            const clientesFormateados = (data || []).map(cliente => ({
                codCliente: normalizeEmpty(cliente.codCliente),
                nomCliente: normalizeEmpty(cliente.nomCliente) + (normalizeEmpty(cliente.lugar) ? " - " + normalizeEmpty(cliente.lugar) : "")
            }));

            if (clientesFormateados.length === 0) {
                $select.empty().prop("disabled", true);
                return;
            }

            llenarSelectConResultados($select, clientesFormateados, "codCliente", "nomCliente");
            $select.prop("disabled", false);
        });
    }

    $("#filtroClienteOrigen").on("keyup", function () {
        buscarClientesPara("#clienteOrigen", $(this).val());
    });

    $("#filtroClienteDestino").on("keyup", function () {
        buscarClientesPara("#clienteDestino", $(this).val());
    });

    $("#btnCopiarDescuentos").on("click", function (e) {
        e.preventDefault();
        esCopia = true;
        showModal('modalCopiarDescuentos');
    });

    $("#btnCerrarModalCopiar, #btnCerrarCopiarDescuentos").on("click", function () {
        esCopia = false;
        hideModal("modalCopiarDescuentos");
    });

    $("#btnAceptarCopiar").on("click", function () {
        const clienteOrigen = $("#clienteOrigen").val();
        const clienteDestino = $("#clienteDestino").val();

        if (!clienteOrigen || !clienteDestino) {
            alert("Debe seleccionar tanto el cliente origen como el cliente destino.");
            return;
        }

        const clienteDestinoText = $("#clienteDestino option:selected").text();

        if ($(`#CodCliente option[value="${clienteDestino}"]`).length === 0) {
            $("#CodCliente").append($('<option>', {
                value: clienteDestino,
                text: clienteDestinoText
            }));
            $("#filtroCliente").val("");
        }

        $("#CodCliente")
            .prop("disabled", false)
            .val(clienteDestino)
            .trigger("change");

        validarBotonCrear();

        $.getJSON(appUrl('Predescuentos/GetDescuentosCombinados'), {
            clienteOrigen,
            clienteDestino,
            tipoDescuento: $('#Tipodescuento').val() || ''
        })
            .done(function (data) {
                const arr = Array.isArray(data) ? data : [];
                refrescarTablaDetalles(arr);

                esCopia = false;
                hideModal("modalCopiarDescuentos");
            })
            .fail(function () {
                alert("Error al copiar descuentos.");
            });
    });

    // =========================================================
    //  EDITAR DETALLE (modal + guardado + eliminar)
    // =========================================================
    function guardarCambiosDetalle() {
        const indexRaw = $('#detalleEditarIndex').val();
        const index = Number(indexRaw);

        if (indexRaw === '' || isNaN(index) || !detalles[index]) {
            alert('Índice no válido.');
            return;
        }

        const codLinea = normalizeEmpty($('#CodLineaEditar').val());
        const desLinea = ($('#CodLineaEditar option:selected').text() || '').split(' - ').slice(1).join(' - ') || '';

        const codClase = normalizeEmpty($('#CodClaseEditar').val());
        const codArticulo = normalizeEmpty($('#CodArticuloEditar').val());

        const tipo = normalizeEmpty($('#TipoEditar').val());
        const valorRaw = $('#ValorEditar').val();
        const valor = parseFloat(valorRaw);

        if (!codLinea || !tipo) {
            alert('Debe indicar al menos Línea y Tipo.');
            return;
        }

        if (valorRaw === '' || isNaN(valor)) {
            alert('Debe indicar un valor numérico válido.');
            return;
        }

        if (valor < 0) {
            alert('⚠️ El valor no puede ser negativo.');
            $('#ValorEditar').focus();
            return;
        }

        // Duplicados
        const duplicadoArticulo = codArticulo && detalles.some((d, i) =>
            i !== index &&
            safeTrim(d.codLinea) === codLinea &&
            safeTrim(d.codArticulo) === codArticulo
        );
        if (duplicadoArticulo) {
            alert('⚠️ Ya existe un detalle con la misma Línea y Artículo.');
            return;
        }

        const duplicadoClase = !codArticulo && codClase && detalles.some((d, i) =>
            i !== index &&
            safeTrim(d.codLinea) === codLinea &&
            safeTrim(d.claseart) === codClase &&
            safeTrim(d.codArticulo) === ''
        );
        if (duplicadoClase) {
            alert('⚠️ Ya existe un detalle con la misma Línea y Clase (sin artículo).');
            return;
        }

        const anterior = detalles[index] || {};

        // desArticulo: tomar del option seleccionado o del lookup
        let desArticulo = '';
        if (codArticulo) {
            const txt = ($('#CodArticuloEditar option:selected').text() || '');
            if (txt.includes(' - ')) desArticulo = txt.split(' - ').slice(1).join(' - ');
            if (!desArticulo) {
                const meta = articuloByCod.get(codArticulo);
                desArticulo = meta ? meta.desArticulo : '';
            }
        }

        detalles[index] = sanitizeDetalle({
            Consecutivodetalle: anterior.Consecutivodetalle || 0,
            codLinea,
            desLinea,
            codArticulo,
            desArticulo,
            claseart: codClase,
            tipo,
            valor
        });

        refrescarTablaDetalles(detalles);
        hideModal('modalEditarDetalle');
    }

    $("#btnGuardarCambiosDetalle").on("click", guardarCambiosDetalle);

    $(document).on('click', '.btn-editar-detalle', function (e) {
        e.preventDefault();
        e.stopPropagation();

        const index = Number($(this).data('index'));
        const detalle = detalles[index];

        if (isNaN(index) || index < 0 || !detalle) {
            alert("No se encontró el detalle a editar.");
            return;
        }

        $('#detalleEditarIndex').val(index);

        $('#filtroLineaEditar, #CodLineaEditar, #BuscarClaseEditar, #CodClaseEditar, #filtroArticuloEditar, #CodArticuloEditar')
            .prop('disabled', false);

        $('#TipoEditar').val(detalle.tipo || 'P');
        $('#ValorEditar').val(detalle.valor ?? '');

        $("#CodLineaEditar, #CodClaseEditar, #CodArticuloEditar").empty().prop("disabled", true);
        $("#filtroLineaEditar").val(detalle.codLinea || '');
        $("#BuscarClaseEditar").val('');
        $("#filtroArticuloEditar").val('');

        showModal('modalEditarDetalle');

        $.getJSON(appUrl('Predescuentos/BuscarLineas'), paramsCatalogoElegible(detalle.codLinea || ''))
            .done(function (data) {
                const $selectLinea = $("#CodLineaEditar");
                $selectLinea.empty();

                if (!Array.isArray(data) || data.length === 0) return;

                $selectLinea.append($('<option>', { value: '', text: '' }));
                data.forEach(item => {
                    $selectLinea.append($('<option>', {
                        value: item.codLinea,
                        text: `${item.codLinea} - ${item.desLinea}`
                    }));
                });

                $selectLinea.prop("disabled", false).val(detalle.codLinea || '').trigger('change');

                // Si es clase
                if (safeTrim(detalle.claseart) !== '') {
                    const filtro = detalle.claseart;
                    const codLinea = detalle.codLinea;
                    const $selectClase = $("#CodClaseEditar");

                    $.getJSON(appUrl('Predescuentos/BuscarClaseartsPorlinea'), paramsClaseElegible(codLinea, filtro))
                        .done(function (dataClase) {
                            $selectClase.empty().prop("disabled", false);
                            if (!Array.isArray(dataClase) || dataClase.length === 0) return;

                            $selectClase.append($('<option>', { value: '', text: '', selected: true }));
                            dataClase.forEach(item => {
                                $selectClase.append($('<option>', {
                                    value: item.codigo,
                                    text: item.codigo + ' - ' + item.descripcion
                                }));
                            });

                            $selectClase.val(detalle.claseart);
                            $('#BuscarClaseEditar').val(detalle.claseart).prop('disabled', false);
                        });
                }
                // Si es artículo
                else if (safeTrim(detalle.codArticulo) !== '') {
                    const filtro = detalle.codArticulo;
                    const codLinea = detalle.codLinea;
                    const $selectArticulo = $("#CodArticuloEditar");

                    $.getJSON(appUrl('Predescuentos/BuscarArticulosPorLinea'), {
                        codLinea,
                        filtro,
                        codCliente: getCodCliente(),
                        buNombre: getBuNombre(),
                        tipoDescuento: $('#Tipodescuento').val() || ''
                    })
                        .done(function (dataArt) {
                            $selectArticulo.empty().prop("disabled", false);

                            if (!Array.isArray(dataArt) || dataArt.length === 0) return;

                            $selectArticulo.append($('<option>', { value: '', text: '', selected: true }));
                            dataArt.forEach(item => {
                                $selectArticulo.append($('<option>', {
                                    value: item.codArticulo,
                                    text: item.codArticulo + ' - ' + item.desArticulo
                                }));
                            });

                            $selectArticulo.val(detalle.codArticulo);
                            $('#filtroArticuloEditar').val(detalle.codArticulo).prop('disabled', false);
                        });
                }
            });
    });

    $("#BuscarClaseEditar").on("input", function () {
        if ($(this).val().trim() !== '') {
            $("#CodArticuloEditar").val("").prop("disabled", true);
            $("#filtroArticuloEditar").val("").prop("disabled", false);
        }
    });

    $("#filtroArticuloEditar").on("input", function () {
        if ($(this).val().trim() !== '') {
            $("#CodClaseEditar").val("").prop("disabled", true);
            $("#BuscarClaseEditar").val("").prop("disabled", false);
        }
    });

    $("#filtroLineaEditar").on("keyup", function () {
        const filtro = $(this).val().trim();
        const $selectLinea = $("#CodLineaEditar");

        if (filtro.length < 2) {
            $selectLinea.empty().prop("disabled", true);
            return;
        }

        $.getJSON(appUrl('Predescuentos/BuscarLineas'), paramsCatalogoElegible(filtro), function (data) {
            $selectLinea.empty();

            if (!Array.isArray(data) || data.length === 0) {
                $selectLinea.prop("disabled", true);
                return;
            }

            $selectLinea.append($('<option>', {
                value: '',
                text: '-- Seleccione una línea --',
                selected: true
            }));

            data.forEach(item => {
                $selectLinea.append($('<option>', {
                    value: item.codLinea,
                    text: `${item.codLinea} - ${item.desLinea || ''}`
                }));
            });

            $selectLinea.prop("disabled", false);
        });
    });

    $("#CodLineaEditar").on("change", function () {
        $("#CodArticuloEditar, #CodClaseEditar").empty().prop("disabled", true);
        $("#filtroArticuloEditar").val('');
        $("#BuscarClaseEditar").val('');
    });

    $("#filtroArticuloEditar").on("keyup", function () {
        const filtro = $(this).val().trim();
        const codLinea = $("#CodLineaEditar").val();
        const $selectArticulo = $("#CodArticuloEditar");
        const $inputClase = $("#BuscarClaseEditar");
        const $selectClase = $("#CodClaseEditar");

        if (!codLinea || filtro.length < 2) {
            $selectArticulo.empty().prop("disabled", true);
            return;
        }

        $.getJSON(appUrl('Predescuentos/BuscarArticulosPorLinea'), {
            codLinea,
            filtro,
            codCliente: getCodCliente(),
            buNombre: getBuNombre(),
            tipoDescuento: $('#Tipodescuento').val() || ''
        }, function (data) {
            $selectArticulo.empty();

            if (!Array.isArray(data) || data.length === 0) {
                $selectArticulo.prop("disabled", true);
                return;
            }

            $selectArticulo.append($('<option>', { value: '', text: '', selected: true }));
            data.forEach(item => {
                $selectArticulo.append($('<option>', {
                    value: item.codArticulo,
                    text: `${item.codArticulo} - ${item.desArticulo || ''}`
                }));
            });

            $selectArticulo.prop("disabled", false);

            if ($inputClase.val().trim() === '') {
                $selectClase.empty().prop("disabled", true).val('');
            }
        });
    });

    $("#BuscarClaseEditar").on("keyup", function () {
        const filtro = $(this).val().trim();
        const codLinea = $("#CodLineaEditar").val();
        const $selectClase = $("#CodClaseEditar");
        const $inputArticulo = $("#filtroArticuloEditar");
        const $selectArticulo = $("#CodArticuloEditar");

        if (!codLinea || filtro.length < 2) {
            $selectClase.empty().prop("disabled", true);
            return;
        }

        $.getJSON(appUrl('Predescuentos/BuscarClaseartsPorlinea'), paramsClaseElegible(codLinea, filtro), function (data) {
            $selectClase.empty();

            if (!Array.isArray(data) || data.length === 0) {
                $selectClase.prop("disabled", true);
                return;
            }

            $selectClase.append($('<option>', { value: '', text: '', selected: true }));
            data.forEach(item => {
                $selectClase.append($('<option>', {
                    value: item.codigo,
                    text: `${item.codigo} - ${item.descripcion || ''}`
                }));
            });

            $selectClase.prop("disabled", false);

            if ($inputArticulo.val().trim() === '') {
                $selectArticulo.empty().prop("disabled", true).val('');
            }
        });
    });

    $('#btnEliminarDetalle').on('click', function () {
        const indexRaw = $('#detalleEditarIndex').val();
        const index = Number(indexRaw);

        if (indexRaw === '' || isNaN(index)) return alert('Índice no válido.');
        if (!confirm('¿Estás seguro de que deseas eliminar este detalle?')) return;

        detalles.splice(index, 1);
        refrescarTablaDetalles(detalles);
        hideModal('modalEditarDetalle');
    });

    // =========================================================
    //  DETALLE ARTICULO (click en fila)
    // =========================================================
    // =========================================================
    //  DETALLE ARTICULO / CLASE / LINEA (click en fila)
    // =========================================================
    $(document).on('click', '#tabla-detalles tbody tr', function (e) {

        // Si hicieron click en el botón Modificar (o dentro), NO abrir detalle
        if ($(e.target).closest('.btn-editar-detalle, button, a').length) return;

        const codArticulo = normalizeEmpty($(this).data('codarticulo'));
        const codLinea = normalizeEmpty($(this).data('codlinea'));   // siempre debería venir
        const codClase = normalizeEmpty($(this).data('claseart'));

        if (!codLinea) return; // línea es obligatoria

        $.getJSON(appUrl('Predescuentos/GetDetalleArticulo'), {
            codArticulo: codArticulo || '',
            codLinea: codLinea || '',
            codClase: codClase || ''
        })
            .done(function (resp) {

                // Compatibilidad: si el server devolviera data directo sin {success,data}
                const data = (resp && resp.data) ? resp.data : (resp || {});
                const ok = (resp && typeof resp.success !== 'undefined') ? !!resp.success : true;

                const lineaTxt = (data.codLinea || codLinea) + (data.desLinea ? ' - ' + data.desLinea : '');
                const claseTxt = (data.codClase || codClase) + (data.desClase ? ' - ' + data.desClase : '');
                const artTxt = (data.codArticulo || codArticulo) + (data.desArticulo ? ' - ' + data.desArticulo : '');

                $('#detalleCodLinea').text(lineaTxt);
                $('#detalleCodClase').text(claseTxt);
                $('#detalleCodArticulo').text(artTxt);

                // Aunque no haya encontrado descripciones, mostramos el modal con lo que haya
                showModal('modalDetalleArticulo');

                // Si querés avisar cuando no hubo match real:
                // if (!ok) console.warn(resp.message || "No se encontró info en BD, mostrando solo códigos.");
            })
            .fail(function (xhr) {
                console.error('GetDetalleArticulo error:', xhr.responseText || xhr.statusText);

                // Fallback: mostrar al menos los códigos
                $('#detalleCodLinea').text(codLinea);
                $('#detalleCodClase').text(codClase);
                $('#detalleCodArticulo').text(codArticulo);
                showModal('modalDetalleArticulo');
            });
    });


    $("#btnCerrarDetalleArticulo").on("click", function () {
        hideModal('modalDetalleArticulo');
    });

    // =========================================================
    //  DECLARACIONES / OTROS HANDLERS
    // =========================================================
    $("#CodCliente").on("change", function () {
        $("#CodCia").val("LANCO_CR");
        validarBotonCrear();
        actualizarBotonesAccion();    // habilita Traer/Copiar según estado
    });

    $("#Estado").on("change", function () {
        actualizarBotonesAccion();
    });

    // Guardar tipo actual para poder revertir si el usuario cancela
    let lastTipoDescuento = $("#Tipodescuento").val();

    // Limpia SOLO la lista principal (tabla-detalles) y estados relacionados
    function limpiarListaPorCambioTipo() {
        detalles = [];
        $("#tabla-detalles tbody").empty();

        // Si venía de copia inicial, ya no aplica cuando cambiás el tipo
        bloqueoTraerDescuentos = false;
        esCopia = false;

        actualizarBotonesAccion();
        actualizarBotonesPorDetalles();
        validarBotonCrear();
    }

    // Reemplazá tu handler actual por este:
    $("#Tipodescuento").on("change", function () {
        const nuevo = $("#Tipodescuento").val();

        // Si no cambió realmente, no hagas nada
        if (nuevo === lastTipoDescuento) return;

        // Si hay detalles, confirmar (si cancela, revertimos el select)
        if (Array.isArray(detalles) && detalles.length > 0) {
            const ok = confirm("Cambiar el tipo de descuento borrará la lista de detalles. ¿Deseás continuar?");
            if (!ok) {
                $("#Tipodescuento").val(lastTipoDescuento);
                return;
            }
        }

        // ✅ limpiar lista al cambiar tipo
        limpiarListaPorCambioTipo();

        // Actualizar fechas (tu lógica actual)
        actualizarEstadoFechas();

        // Si está visible la sección de artículos en el modal, recalcular con el tipo nuevo
        if ($("#seccionArticuloChecklist").is(":visible")) {
            renderArticulosChecklist();
        }

        // actualizar el “tipo anterior”
        lastTipoDescuento = nuevo;
    });

    $("#Tipodescuento").on("change", function () {
        actualizarEstadoFechas();

        if (detalles.length > 0) {
            const ok = confirm("Cambiar el tipo de descuento limpiará la lista. ¿Continuar?");
            if (!ok) return;
        }

        detalles = [];
        refrescarTablaDetalles(detalles);

        // 🔥 IMPORTANTÍSIMO: si venías de copia, liberá el bloqueo
        bloqueoTraerDescuentos = false;
        esCopia = false;

        actualizarBotonesAccion();
    });

    $("#btnCrear").on("click", function () {
        $("#formCrear").trigger("submit");
    });

    // =========================================================
    //  COPIA INICIAL DESDE SOLICITUD (Create?copiarDeConsecutivo=...)
    //  Requiere que la vista defina:
    //  window.detallesCopia = @Html.Raw(ViewBag.DetallesCopiaJson ?? "[]");
    // =========================================================
    (function initCopiaInicial() {
        if (!window.detallesCopia || !Array.isArray(window.detallesCopia) || window.detallesCopia.length === 0) return;

        bloqueoTraerDescuentos = true;
        actualizarBotonesAccion();
        refrescarTablaDetalles(window.detallesCopia);
    })();

    // =========================================================
    //  INIT
    // =========================================================
    validarBotonCrear();
    actualizarEstadoFechas();
    actualizarBotonesPorDetalles();
});
