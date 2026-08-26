// ~/js/EditDeclaraciones.js
// Edit con la MISMA funcionalidad de Create:
//
// ✅ FIX PRINCIPAL:
//   - El controller Edit (GET) te manda DetallesJson con PascalCase (CodLinea, Tipo, Valor, etc.)
//   - El JS antes leía solo camelCase (codLinea, tipo, valor) => todo quedaba vacío y valor 0.
//   - Ahora el JS:
//       1) Lee ambos (PascalCase y camelCase)
//       2) Siempre escribe el hidden DetallesJson en PascalCase (lo que tu POST Edit espera)
//
// - Agregar por Línea / Artículo / Clase (checklists)
// - Editar detalle (modal)
// - Detalle artículo (click fila)
// - Copiar descuentos (origen -> cliente actual)
// - PROMO: BuscarArticulosPorLinea filtra por XXORA END_DATE NULL + (BU, Cliente)
// - Medida + descripción en artículos
// - Bloquea negativos
// - Mantiene DetallesJson actualizado para el POST de Edit

let detalles = [];
let esCopia = false;
let filtroClienteTimeout = null;
let filtroArticuloTimeout = null;

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

    const articuloByCod = new Map(
        articulosAll
            .filter(a => a && a.codArticulo)
            .map(a => [String(a.codArticulo).trim(), {
                medida: (a.medida ?? '').toString().trim(),
                codLinea: (a.codLinea ?? '').toString().trim(),
                desLinea: (a.desLinea ?? '').toString().trim(),
                desArticulo: (a.desArticulo ?? '').toString().trim()
            }])
    );

    function safeTrim(v) {
        return (v === undefined || v === null) ? '' : String(v).trim();
    }

    function normalizeEmpty(v) {
        const t = safeTrim(v);
        return t.length ? t : '';
    }

    function escAttr(s) {
        return String(s ?? '').replace(/"/g, '&quot;');
    }

    // Lee la primera propiedad existente (sirve para PascalCase/camelCase mezclado)
    function pick(obj, ...keys) {
        for (const k of keys) {
            if (obj && obj[k] !== undefined && obj[k] !== null) return obj[k];
        }
        return undefined;
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

    // =========================================================
    //  MODALES (BS5/BS4)
    // =========================================================
    function showModal(id) {
        const el = document.getElementById(id);
        if (!el) return;

        if (window.bootstrap && bootstrap.Modal) {
            bootstrap.Modal.getOrCreateInstance(el).show();
            return;
        }
        if (window.$ && $(el).modal) {
            $(el).modal('show');
        }
    }

    function hideModal(id) {
        const el = document.getElementById(id);
        if (!el) return;

        if (window.bootstrap && bootstrap.Modal) {
            const m = bootstrap.Modal.getInstance(el);
            if (m) m.hide();
            return;
        }
        if (window.$ && $(el).modal) {
            $(el).modal('hide');
        }
    }

    // =========================================================
    //  JSON hidden (Edit POST)
    //  IMPORTANTÍSIMO:
    //    Tu POST Edit parsea TryGetProperty("CodLinea","Tipo","Valor","Claseart","CodArticulo")
    //    => Aquí SIEMPRE lo mandamos en PascalCase.
    // =========================================================
    function toDetalleDto(d) {
        return {
            Consecutivodetalle: d.Consecutivodetalle || 0,
            CodLinea: normalizeEmpty(d.codLinea),
            CodArticulo: normalizeEmpty(d.codArticulo),
            Claseart: normalizeEmpty(d.claseart),
            Tipo: normalizeEmpty(d.tipo) || 'P',
            Valor: Number.isFinite(Number(d.valor)) ? Number(d.valor) : 0
        };
    }

    function syncDetallesJsonHidden() {
        const payload = detalles.map(toDetalleDto);
        $('#DetallesJson').val(JSON.stringify(payload));
    }

    // =========================================================
    //  TABLA DETALLES
    //  Lee tanto camelCase como PascalCase
    // =========================================================
    function sanitizeDetalle(d) {
        const codLinea = normalizeEmpty(pick(d, 'codLinea', 'CodLinea', 'COD_LINEA'));
        const desLinea = normalizeEmpty(pick(d, 'desLinea', 'DesLinea', 'DES_LINEA'));

        const codArticulo = normalizeEmpty(pick(d, 'codArticulo', 'CodArticulo', 'COD_ARTICULO'));
        const desArticulo = normalizeEmpty(pick(d, 'desArticulo', 'DesArticulo', 'DES_ARTICULO'));

        // En algunos JSON viene "Claseart", en otros "claseart" o "COD_CLASE"
        const claseart = normalizeEmpty(pick(d, 'claseart', 'Claseart', 'COD_CLASE', 'CodClase'));
        const tipo = normalizeEmpty(pick(d, 'tipo', 'Tipo', 'TIPO')) || 'P';

        let valor = pick(d, 'valor', 'Valor', 'VALOR');
        if (valor === null || valor === undefined || valor === '') valor = 0;
        const valorNum = Number(valor);
        valor = Number.isFinite(valorNum) ? valorNum : 0;

        return {
            Consecutivodetalle: pick(d, 'Consecutivodetalle', 'consecutivodetalle', 'CONSECUTIVODETALLE') || 0,
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

        detalles = arr.map(sanitizeDetalle);

        let tablaHtml = '';
        detalles.forEach((item, i) => {
            tablaHtml += `
                <tr
                    data-codarticulo="${escAttr(item.codArticulo || '')}"
                    data-codlinea="${escAttr(item.codLinea || '')}"
                    data-claseart="${escAttr(item.claseart || '')}">
                    <td>${escAttr(item.codLinea)}${item.desLinea ? ' - ' + escAttr(item.desLinea) : ''}</td>
                    <td>${escAttr(item.codArticulo || '')}${item.desArticulo ? ' - ' + escAttr(item.desArticulo) : ''}</td>
                    <td>${item.tipo === 'P' ? 'Porcentaje' : item.tipo === 'M' ? 'Monto' : escAttr(item.tipo || '')}</td>
                    <td>${escAttr(item.valor)}</td>
                    <td>${escAttr(item.claseart || '')}</td>
                    <td>
                        <button type="button" class="btn btn-danger btn-sm btn-editar-detalle" data-index="${i}">
                            Modificar
                        </button>
                    </td>
                </tr>`;
        });

        $('#tabla-detalles tbody').html(tablaHtml);
        syncDetallesJsonHidden();
        reindexDetalles();
        actualizarBloqueoTraerCopiar();
    }

    function reindexDetalles() {
        $("#tabla-detalles tbody tr").each(function (i, row) {
            $(row).find(".btn-editar-detalle").attr("data-index", i);
        });
    }

    // =========================================================
    //  FECHAS según tipo
    // =========================================================
    function actualizarEstadoFechas() {
        const tipo = $("#Tipodescuento").val();

        if (tipo === "Descuento Fijo") {
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

    // =========================================================
    //  TRAER DESCUENTOS (cliente actual)
    // =========================================================
    $("#btnTraerDescuentos").on("click", function () {

        if (!validarNoHayDetallesParaAccion("traer descuentos")) return;

        const codCliente = getCodCliente();
        if (!codCliente) {
            alert("No hay cliente en la solicitud.");
            return;
        }

        $.getJSON(appUrl('Predescuentos/GetDescuentosCliente'), { codCliente })
            .done(function (data) {
                const arr = Array.isArray(data) ? data : [];
                refrescarTablaDetalles(arr);
            })
            .fail(function () {
                alert("Error al traer los descuentos del cliente.");
            });
    });

    // =========================================================
    //  VALIDAR tipo/valor manual (no negativos)
    // =========================================================
    function obtenerTipoValorManual() {
        const tipo = $("#manualTipo").val();
        const valorRaw = $("#manualValor").val();
        const valor = parseFloat(valorRaw);

        if (!tipo) {
            alert("Seleccione el tipo.");
            return null;
        }
        if (valorRaw === "" || isNaN(valor)) {
            alert("Ingrese un valor numérico válido.");
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

    // =========================================================
    //  CHECKLIST LÍNEAS
    // =========================================================
    function cargarLineasChecklist() {
        const $tbody = $("#tbodyLineasChecklist");
        $tbody.empty();
        $("#chkLineaTodo").prop("checked", false);

        $.getJSON(appUrl('Predescuentos/BuscarLineas'), { filtro: "" })
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

    function resetSelectEmpty($select) {
        $select.empty().append($('<option>', { value: '', text: '', selected: true }));
    }

    function limpiarClaseEditarUI() {
        $("#BuscarClaseEditar").val('');
        resetSelectEmpty($("#CodClaseEditar"));
        $("#CodClaseEditar").prop("disabled", true);
    }

    function limpiarArticuloEditarUI() {
        $("#filtroArticuloEditar").val('');
        resetSelectEmpty($("#CodArticuloEditar"));
        $("#CodArticuloEditar").prop("disabled", true);
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
    //  CHECKLIST ARTÍCULOS
    //  - Si hay cache (articulosAll) y no es promo => usa cache
    //  - Si NO hay cache, usa server también para FIJO (misma ruta)
    // =========================================================
    function getDesLineaDesdeSelect(codLinea) {
        if (!codLinea) return "";
        const txt = $(`#CodLineaArticulo option[value="${codLinea}"]`).text() || "";
        return txt.includes(" - ") ? txt.split(" - ").slice(1).join(" - ") : txt;
    }

    function renderArticulosChecklistDesdeCache() {
        const filtroLinea = safeTrim($("#CodLineaArticulo").val());
        const filtroMedida = safeTrim($("#filtroMedidaArticulo").val());
        const likeRaw = safeTrim($("#filtroDescArticulo").val());
        const like = likeRaw.toUpperCase();

        const $tbody = $("#tbodyArticulosChecklist");
        $tbody.empty();
        $("#chkArticuloTodo").prop("checked", false);

        let data = articulosAll.slice();

        if (filtroLinea) data = data.filter(a => safeTrim(a.codLinea) === filtroLinea);
        if (filtroMedida) data = data.filter(a => safeTrim(a.medida) === filtroMedida);

        if (like.length >= 2) {
            data = data.filter(a =>
                safeTrim(a.desArticulo).toUpperCase().includes(like) ||
                safeTrim(a.codArticulo).toUpperCase().includes(like)
            );
        }

        const hayAlgunFiltro = !!(filtroLinea || filtroMedida || likeRaw);
        if (!hayAlgunFiltro) {
            $tbody.append('<tr><td colspan="3" class="text-center text-muted">Use Línea, Medida o Descripción para buscar.</td></tr>');
            return;
        }

        const MAX = 400;
        if (data.length > MAX) data = data.slice(0, MAX);

        if (data.length === 0) {
            $tbody.append('<tr><td colspan="3" class="text-center text-muted">Sin resultados</td></tr>');
            return;
        }

        data.forEach(item => {
            const codLineaItem = safeTrim(item.codLinea);
            const desLineaItem = safeTrim(item.desLinea) || getDesLineaDesdeSelect(codLineaItem);

            const codArticulo = safeTrim(item.codArticulo);
            const desArticulo = safeTrim(item.desArticulo);

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
    }

    function renderArticulosChecklistDesdeServer() {
        const codLineaSel = safeTrim($("#CodLineaArticulo").val());
        const filtroMedida = safeTrim($("#filtroMedidaArticulo").val());
        const filtroTxt = safeTrim($("#filtroDescArticulo").val());

        const $tbody = $("#tbodyArticulosChecklist");
        $tbody.empty();
        $("#chkArticuloTodo").prop("checked", false);

        const hayFiltroTexto = filtroTxt.length >= 2;
        const hayLinea = !!codLineaSel;
        const hayMedida = !!filtroMedida;

        if (!hayLinea && !hayMedida && !hayFiltroTexto) {
            $tbody.append('<tr><td colspan="3" class="text-center text-muted">Use Línea, Medida o Descripción para buscar.</td></tr>');
            return;
        }

        // Para promo el server exige codCliente, para fijo no, pero lo mandamos igual si existe
        const codCliente = getCodCliente();

        $.getJSON(appUrl('Predescuentos/BuscarArticulosPorLinea'), {
            codLinea: codLineaSel || '',
            filtro: filtroTxt || '',
            medida: filtroMedida || '',
            codCliente: codCliente || '',
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
        // Promo => server sí o sí
        if (esPromocional()) {
            renderArticulosChecklistDesdeServer();
            return;
        }
        // Fijo => cache si existe, si no server (así Edit no depende de ViewBag.ArticulosJson)
        if (Array.isArray(articulosAll) && articulosAll.length > 0) {
            renderArticulosChecklistDesdeCache();
        } else {
            renderArticulosChecklistDesdeServer();
        }
    }

    // =========================================================
    //  BLOQUEO: Traer/Copiar si ya hay detalles en el JSON
    // =========================================================
    function hayDetallesEnJson() {
        return Array.isArray(detalles) && detalles.length > 0;
    }

    function actualizarBloqueoTraerCopiar() {
        const bloquear = hayDetallesEnJson();

        $("#btnTraerDescuentos").prop("disabled", bloquear).toggleClass("disabled", bloquear);
        $("#btnCopiarDescuentos").prop("disabled", bloquear).toggleClass("disabled", bloquear);

        // Por si el modal de copiar ya estaba abierto
        $("#btnAceptarCopiar").prop("disabled", bloquear).toggleClass("disabled", bloquear);
    }

    function validarNoHayDetallesParaAccion(nombreAccion) {
        if (hayDetallesEnJson()) {
            alert(`No se puede ${nombreAccion} porque ya hay detalles en la lista. Si quiere hacerlo, primero use "Limpiar Lista".`);
            return false;
        }
        return true;
    }


    $("#filtroLineaArticulo").on("keyup", function () {
        const filtro = $(this).val().trim();
        const $selectLinea = $("#CodLineaArticulo");

        $selectLinea.empty()
            .append('<option value="">Seleccione una línea...</option>')
            .prop("disabled", true);

        $("#tbodyArticulosChecklist").empty();
        $("#chkArticuloTodo").prop("checked", false);

        if (filtro.length < 2) return;

        $.getJSON(appUrl('/Predescuentos/BuscarLineas'), { filtro })
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

    $("#CodLineaArticulo").on("change", function () {
        $("#filtroDescArticulo").val("");
        renderArticulosChecklist();
    });

    $("#filtroMedidaArticulo").on("change", renderArticulosChecklist);

    $("#filtroDescArticulo").on("input", function () {
        clearTimeout(filtroArticuloTimeout);
        filtroArticuloTimeout = setTimeout(renderArticulosChecklist, 250);
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
    //  CHECKLIST CLASES
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

        $.getJSON(appUrl('/Predescuentos/BuscarLineas'), { filtro }, function (data) {
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

        $.getJSON(appUrl('Predescuentos/BuscarClaseartsPorlinea'), { codLinea, filtro: "" }, function (data) {
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
    //  ABRIR MODAL AGREGAR (3 botones)
    // =========================================================
    function abrirModalAgregar(modo) {
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
            $("#tbodyArticulosChecklist").html('<tr><td colspan="3" class="text-center text-muted">Use Línea, Medida o Descripción para buscar.</td></tr>');
        } else if (modo === "clase") {
            $("#seccionClaseChecklist").show();
            $("#btnAgregarClasesChecklist").show();
            $("#modalLabelManual").text("Agregar descuentos por Clase");

            $("#filtroLineaClase").val("");
            $("#CodLineaClase").empty()
                .append('<option value="">Seleccione una línea...</option>')
                .prop("disabled", true);

            $("#tbodyClasesChecklist").html(
                '<tr><td colspan="3" class="text-center text-muted">Seleccione una línea para comenzar.</td></tr>'
            );
        }

        showModal('modalAgregarManual');
    }

    $("#btnAgregarLinea").on("click", () => abrirModalAgregar("linea"));
    $("#btnAgregarArticulo").on("click", () => abrirModalAgregar("articulo"));
    $("#btnAgregarClase").on("click", () => abrirModalAgregar("clase"));

    // =========================================================
    //  LIMPIAR LISTA
    // =========================================================
    $("#btnLimpiarLista").on("click", function () {
        if (!confirm("¿Estás seguro de que deseas borrar todos los elementos de la lista?")) return;
        detalles = [];
        refrescarTablaDetalles(detalles);
    });

    // =========================================================
    //  COPIAR DESCUENTOS (origen -> cliente actual)
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
                // OJO: según configuración del serializer, puede venir camelCase (codCliente) o PascalCase (CodCliente)
                codCliente: normalizeEmpty(pick(cliente, 'codCliente', 'CodCliente')),
                nomCliente: normalizeEmpty(pick(cliente, 'nomCliente', 'NomCliente')) +
                    (normalizeEmpty(pick(cliente, 'lugar', 'Lugar')) ? " - " + normalizeEmpty(pick(cliente, 'lugar', 'Lugar')) : "")
            }));

            if (clientesFormateados.length === 0) {
                $select.empty().prop("disabled", true);
                return;
            }

            llenarSelectConResultados($select, clientesFormateados, "codCliente", "nomCliente");
            $select.prop("disabled", false);
        });
    }

    $("#btnCopiarDescuentos").on("click", function (e) {
        e.preventDefault();

        if (!validarNoHayDetallesParaAccion("copiar descuentos")) return;

        esCopia = true;
        showModal('modalCopiarDescuentos');
    });

    $("#btnCerrarModalCopiar, #btnCerrarCopiarDescuentos").on("click", function () {
        esCopia = false;
        hideModal("modalCopiarDescuentos");
    });

    $("#filtroClienteOrigen").on("keyup", function () {
        buscarClientesPara("#clienteOrigen", $(this).val());
    });

    $("#btnAceptarCopiar").on("click", function () {

        if (!validarNoHayDetallesParaAccion("copiar descuentos")) return;

        const clienteOrigen = $("#clienteOrigen").val();
        const clienteDestino = getCodCliente();

        if (!clienteOrigen) {
            alert("Debe seleccionar el cliente origen.");
            return;
        }
        if (!clienteDestino) {
            alert("No hay cliente destino.");
            return;
        }

        $.getJSON(appUrl('Predescuentos/GetDescuentosCombinados'), { clienteOrigen, clienteDestino })
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
    //  EDITAR DETALLE (modal)
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

        const codArticulo = normalizeEmpty($('#CodArticuloEditar').val());
        const codClaseRaw = normalizeEmpty($('#CodClaseEditar').val());

        const codClase = codArticulo ? "" : codClaseRaw;

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

        // duplicados
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

    // Si selecciona un artículo -> limpia clase
    $(document).on("change", "#CodArticuloEditar", function () {
        const v = (String($(this).val() ?? '')).trim();
        if (v) limpiarClaseEditarUI();
    });

    // Si selecciona una clase -> limpia artículo
    $(document).on("change", "#CodClaseEditar", function () {
        const v = (String($(this).val() ?? '')).trim();
        if (v) limpiarArticuloEditarUI();
    });


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

        $('#TipoEditar').val(detalle.tipo || 'P');
        $('#ValorEditar').val(detalle.valor ?? '');

        $("#CodLineaEditar, #CodClaseEditar, #CodArticuloEditar").empty().prop("disabled", true);
        $("#filtroLineaEditar").val(detalle.codLinea || '');
        $("#BuscarClaseEditar").val('');
        $("#filtroArticuloEditar").val('');

        showModal('modalEditarDetalle');

        $.getJSON(appUrl('/Predescuentos/BuscarLineas'), { filtro: detalle.codLinea || '' })
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

                // clase
                if (safeTrim(detalle.claseart) !== '') {
                    const codLinea = detalle.codLinea;
                    const filtro = detalle.claseart;
                    const $selectClase = $("#CodClaseEditar");

                    $.getJSON(appUrl('/Predescuentos/BuscarClaseartsPorlinea'), { codLinea, filtro })
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
                // artículo
                else if (safeTrim(detalle.codArticulo) !== '') {
                    const codLinea = detalle.codLinea;
                    const filtro = detalle.codArticulo;
                    const $selectArticulo = $("#CodArticuloEditar");

                    $.getJSON(appUrl('/Predescuentos/BuscarArticulosPorLinea'), {
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

    $("#filtroLineaEditar").on("keyup", function () {
        const filtro = $(this).val().trim();
        const $selectLinea = $("#CodLineaEditar");

        if (filtro.length < 2) {
            $selectLinea.empty().prop("disabled", true);
            return;
        }

        $.getJSON(appUrl('/Predescuentos/BuscarLineas'), { filtro }, function (data) {
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

        // ✅ si empiezo a buscar artículo, la clase se limpia
        if ($(this).val().trim().length > 0) {
            limpiarClaseEditarUI();
        }

        const filtro = $(this).val().trim();
        const codLinea = $("#CodLineaEditar").val();
        const $selectArticulo = $("#CodArticuloEditar");

        if (!codLinea || filtro.length < 2) {
            $selectArticulo.empty().prop("disabled", true);
            return;
        }

        $.getJSON(appUrl('/Predescuentos/BuscarArticulosPorLinea'), {
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
        });
    });

    $("#BuscarClaseEditar").on("keyup", function () {

        // ✅ si empiezo a buscar clase, el artículo se limpia
        if ($(this).val().trim().length > 0) {
            limpiarArticuloEditarUI();
        }

        const filtro = $(this).val().trim();
        const codLinea = $("#CodLineaEditar").val();
        const $selectClase = $("#CodClaseEditar");

        if (!codLinea || filtro.length < 2) {
            $selectClase.empty().prop("disabled", true);
            return;
        }

        $.getJSON(appUrl('/Predescuentos/BuscarClaseartsPorlinea'), { codLinea, filtro }, function (data) {
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
    //  DETALLE ARTICULO (click fila)
    // =========================================================
    $(document).on('click', '#tabla-detalles tbody tr', function () {
        const codArticulo = normalizeEmpty($(this).data('codarticulo'));
        const codLinea = normalizeEmpty($(this).data('codlinea'));
        const codClase = normalizeEmpty($(this).data('claseart'));

        if (!codArticulo && !codLinea && !codClase) return;

        $.getJSON(appUrl('Predescuentos/GetDetalleArticulo'), {
            codArticulo: codArticulo || '',
            codLinea: codLinea || '',
            codClase: codClase || ''
        }, function (response) {
            if (response && response.success) {
                const data = response.data || {};

                $('#detalleCodArticulo').text((data.codArticulo || '') + (data.desArticulo ? ' - ' + data.desArticulo : ''));
                $('#detalleCodLinea').text((data.codLinea || '') + (data.desLinea ? ' - ' + data.desLinea : ''));
                $('#detalleCodClase').text((data.codClase || '') + (data.desClase ? ' - ' + data.desClase : ''));

                showModal('modalDetalleArticulo');
            } else {
                alert('No se encontró información.');
            }
        }).fail(function () {
            alert('Error al consultar el detalle.');
        });
    });

    // =========================================================
    //  SUBMIT: asegurar DetallesJson al día
    // =========================================================
    $("#formEditar").on("submit", function () {
        syncDetallesJsonHidden();
    });

    // =========================================================
    //  INIT
    // =========================================================
    $("#Tipodescuento").on("change", function () {
        actualizarEstadoFechas();
        if ($("#seccionArticuloChecklist").is(":visible")) {
            renderArticulosChecklist();
        }
    });

    actualizarEstadoFechas();

    // cargar detalles iniciales desde window.detallesIniciales / hidden
    (function initDetalles() {
        let inicial = Array.isArray(window.detallesIniciales) ? window.detallesIniciales : null;

        if (!inicial) {
            const raw = $('#DetallesJson').val();
            try { inicial = JSON.parse(raw || '[]'); } catch { inicial = []; }
        }

        // Normalizar a nuestro shape interno (codLinea/codArticulo/claseart/tipo/valor)
        let base = (Array.isArray(inicial) ? inicial : []).map(sanitizeDetalle);

        // Enriquecer descripciones si faltan y hay cache
        base = base.map(d => {
            const codArticulo = normalizeEmpty(d.codArticulo);
            let desArticulo = normalizeEmpty(d.desArticulo);
            let desLinea = normalizeEmpty(d.desLinea);

            if (codArticulo) {
                const meta = articuloByCod.get(codArticulo);
                if (!desArticulo && meta) desArticulo = meta.desArticulo || '';
                if (!desLinea && meta) desLinea = meta.desLinea || '';
            }

            return {
                ...d,
                desLinea,
                desArticulo 
            };
        });

        refrescarTablaDetalles(base);
    })();

});