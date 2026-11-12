# Revisão técnica — v1.0.0 → v1.1.0

**Compilação: NÃO executada** (sem .NET SDK/Windows no ambiente da revisão). O código foi conferido apenas por leitura e checagem estática simples. A primeira compilação real será a do GitHub Actions.

## Defeitos críticos corrigidos

| # | Problema na v1.0.0 | Correção |
|---|---|---|
| 1 | `INPUT` do SendInput sem `MOUSEINPUT` na união: `sizeof(INPUT)` = 32 bytes em x64 (verificado por cálculo de layout); o Windows exige 40 e `SendInput` retornaria 0 (erro 87). Nenhuma expansão funcionaria. | União com `MOUSEINPUT`, `KEYBDINPUT`, `HARDWAREINPUT`. |
| 2 | **Pausar não pausava**: o motor nunca consultava `Enabled`. | O motor lê `Enabled` do snapshot; pausado, passa tudo e limpa o buffer. |
| 3 | Corrida de dados: a tarefa de expansão limpava `_buffer` enquanto o hook o usava. | Buffer/pendência pertencem só à thread do hook; a expansão só usa `_expanding`, prazo e fila com lock. |
| 4 | Exceção no hook derrubaria o processo; `_expanding` preso bloquearia o teclado inteiro. | `try/catch` no hook; watchdog que libera o bloqueio se a expansão exceder o prazo. |
| 5 | Delimitador pendente sem soltar/rollover: tecla seguinte vazava para o destino antes da expansão; `_pending` obsoleto podia apagar texto depois. | Rollover inicia a expansão e enfileira a tecla; pendência expira em 1,5 s. |
| 6 | Perda de dados no editor: digitar na pesquisa recarregava o editor com outro atalho; trocar de linha descartava edições. | Pesquisa só atualiza a lista; controle de "não salvo" com confirmação; seleção tratada fora do evento (evita reentrância do DataGridView). |
| 7 | `config.json` ilegível era sobrescrito por padrões no próximo salvamento. | Cópia `config.invalido-*.json`, aviso ao usuário; gravação com flush e `config.json.bak`. |
| 8 | `SplitContainer.SplitterDistance=370` antes de ter tamanho final: lista ficava estreita. | `TableLayoutPanel` de duas colunas. |
| 9 | Sem escala por DPI: alturas absolutas cortavam o texto a 125–150%. | `AutoScaleMode.Dpi`; mascote e ícones escalam por `DeviceDpi`. |

## Outras melhorias

- Snapshot imutável (`EngineConfig`) com dicionário de regras: sem lock nem cópia profunda por tecla.
- Teclas mortas do ABNT2 (´ ` ~ ^ ¨): `´ + a` entra no buffer como `á`; tecla morta antes de delimitador não dispara expansão.
- Emojis: as duas metades do par substituto vão no mesmo `SendInput`, sem atraso entre elas.
- Aguarda Shift/Ctrl/Alt/AltGr soltos (máx. 600 ms) antes de injetar Backspace/Enter; verifica se a janela ativa continua a mesma.
- Em falha ao apagar o primeiro caractere, devolve o delimitador engolido.
- Keyups passam durante a expansão (evita modificador preso); só keydowns são retidos e reenviados (até 64 caracteres, em memória).
- Troca de campo de foco (`GetGUIThreadInfo`) também zera o buffer; prefixos só de pontuação (`::x`) são acumulados.
- Hook reinstalado a cada 3 min ocioso e em desbloqueio de sessão/retomada de energia (o Windows pode removê-lo silenciosamente).
- Hotkey registrado em `OnHandleCreated`; hook instalado depois do início do loop de mensagens.
- Gravação adiada (600 ms) para campos que mudam a cada tecla; flush ao sair; falhas registradas e sinalizadas.
- Log thread-safe, com rotação, **sem nomes de atalhos** (v1.0.0 os registrava); registra nome do processo de destino.
- Instância única agora traz a janela existente à frente; início com Windows usa `--tray` e atualiza o caminho do exe.
- Descarte correto de ícones, fontes, bitmaps, timers, bandeja e hook; manipuladores globais de exceção.
- Ícone de bandeja pausado próprio (em vez de `SystemIcons.Warning`); ícones vetoriais próprios nos botões.
- Mascote: recursos GDI+ criados uma vez, 30 fps ao mover / 10 fps parado, timer para quando a janela é ocultada, `HTTRANSPARENT`, olhar 2D.
- Workflow: timeout, concurrency, telemetria da CLI desativada, verificação do exe, SHA-256, sem restore separado (self-contained precisa do restore com os mesmos parâmetros).

## Mantido de propósito (compatibilidade validada)

WH_KEYBOARD_LL, SendInput Unicode, atraso de 10 ms (padrão e `Thread.Sleep` — **não** usei `timeBeginPeriod`, que mudaria o intervalo real), Backspace/Enter/Tab por VK, ausência total de clipboard.

## Riscos a comparar em teste (não validados)

1. **Par substituto em um único SendInput** (emojis): melhora a integridade, mas a PoC só testou texto BMP.
2. **Esperar modificadores soltos** e **reter teclas durante a expansão**: só afeta quem digita rápido ou usa Shift/AltGr no delimitador.
3. **`wScan` = 0 nas teclas virtuais** (como na v1.0.0): se o Backspace/Enter falhar no Citrix, o próximo experimento é enviar o scan code, apenas após teste comparativo.
4. Layout local ≠ layout remoto: não resolvido.

## Arquivos

Alterados: `KeyboardEngine.cs`, `MainForm.cs` (dividido), `Models.cs`, `SettingsStore.cs`, `MousePetControl.cs`, `AppIconFactory.cs`, `Program.cs`, `ExpansorTexto.csproj`, `app.manifest`, `build-windows.yml`, `README.md`, `HISTORICO_VALIDACAO.md`.
Novos: `MainForm.Layout.cs`, `UiHelpers.cs` (inclui `LogoMark` e ícones), `AppLog.cs`, `REVISAO_TECNICA.md`, `.gitignore`.
Inalterado: `PROMPT_PARA_CLAUDE.md`.
