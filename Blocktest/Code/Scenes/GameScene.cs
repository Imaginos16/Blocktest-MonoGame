using System.Linq;
using Blocktest.Block_System;
using Blocktest.Misc;
using Blocktest.Networking;
using Blocktest.Rendering;
using Microsoft.Xna.Framework.Input;
using Shared.Code;
using Shared.Code.Block_System;
using Shared.Code.Packets;
namespace Blocktest.Scenes;

public sealed class GameScene : IScene {
    private readonly bool _connect;
    private readonly FrameCounter _frameCounter = new();
    private readonly BlocktestGame _game;
    private readonly Client _networkingClient;
    private readonly SpriteBatch _spriteBatch;
    private int _blockSelected = 1; //ID of the block to place
    private string[] _blockStrings;

    private readonly RenderableTilemap _backgroundTilemapSprites;
    private readonly RenderableTilemap _foregroundTilemapSprites;
    
    private bool _buildMode = true; //true for build, false for destroy

    private KeyboardState _previousKeyboardState;

    private readonly Camera _camera;

    public GameScene(BlocktestGame game, bool doConnect, string? ip) {
        _connect = doConnect;
        _spriteBatch = new SpriteBatch(game.GraphicsDevice);
        _game = game;

        _camera = new Camera(Vector2.Zero, new Vector2(512, 256), game.GraphicsDevice);

        GlobalsShared.BackgroundTilemap = new TilemapShared(GlobalsShared.MaxX, GlobalsShared.MaxY, true);
        GlobalsShared.ForegroundTilemap = new TilemapShared(GlobalsShared.MaxX, GlobalsShared.MaxY, false);
        _backgroundTilemapSprites = new RenderableTilemap(GlobalsShared.BackgroundTilemap, _camera);
        _foregroundTilemapSprites = new RenderableTilemap(GlobalsShared.ForegroundTilemap, _camera);
        _networkingClient = new Client();
        
        _blockStrings = BlockManagerShared.AllBlocks.Keys.ToArray();
        
        if (_connect && ip != null) {
            _networkingClient.Start(ip, 9050, "testKey");
            return;
        }

        var testDownload = WorldDownload.Default();
        testDownload.Process();
    }

    public void Update(GameTime gameTime) {
        MouseState currentMouseState = Mouse.GetState();
        KeyboardState currentKeyboardState = Keyboard.GetState();

        if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed ||
            currentKeyboardState.IsKeyDown(Keys.Escape)) {
            _game.Exit();
        }

        if (_connect) {
            _networkingClient.Update();
        }
        
        _networkingClient.ClientTickBuffer.IncrCurrTick();

        if (!_game.IsActive) {
            return;
        }

        //press E to toggle build/destroy
        if (currentKeyboardState.IsKeyDown(Keys.E) &&
            _previousKeyboardState.IsKeyUp(Keys.E)) {
            _buildMode = !_buildMode;
        }

        //Q changes which block you have selected
        if (currentKeyboardState.IsKeyDown(Keys.Q) &&
            _previousKeyboardState.IsKeyUp(Keys.Q)) {
            _blockSelected++;
            if (_blockSelected >= BlockManagerShared.AllBlocks.Count) {
                _blockSelected = 1;
            }
        }

        float moveValue = 2.5f;
        if (currentKeyboardState.IsKeyDown(Keys.LeftShift) || currentKeyboardState.IsKeyDown(Keys.RightShift)) {
            moveValue *= 4;
        }

        if (currentKeyboardState.IsKeyDown(Keys.A)) {
            _camera.Position.X -= moveValue;
        } else if (currentKeyboardState.IsKeyDown(Keys.D)) {
            _camera.Position.X += moveValue;
        }

        if (currentKeyboardState.IsKeyDown(Keys.W)) {
            _camera.Position.Y += moveValue;
        } else if (currentKeyboardState.IsKeyDown(Keys.S)) {
            _camera.Position.Y -= moveValue;
        }
        
        _previousKeyboardState = currentKeyboardState;

        if (currentMouseState.LeftButton != ButtonState.Pressed &&
            currentMouseState.RightButton != ButtonState.Pressed ||
            !_camera.RenderLocation.Contains(currentMouseState.Position)) {
            return;
        }

        bool foreground = currentMouseState.LeftButton == ButtonState.Pressed;
        Vector2 mousePos = _camera.CameraToWorldPos(currentMouseState.Position.ToVector2());
        Vector2Int tilePos = new Vector2Int(
            Math.Clamp((int)mousePos.X / GlobalsShared.GridSize.X, 0, GlobalsShared.MaxX),
            Math.Clamp((int)mousePos.Y / GlobalsShared.GridSize.Y, 0, GlobalsShared.MaxY));

        if (_buildMode) {
            TileChange testChange = new() {
                TickNum = _networkingClient.ClientTickBuffer.CurrTick,
                Position = tilePos,
                Foreground = foreground,
                BlockUid = _blockStrings[_blockSelected]
            };

            _networkingClient.ClientTickBuffer.AddPacket(testChange);
            if (_connect) {
                _networkingClient.SendTileChange(testChange);
            }
        } else {
            BreakTile testBreak = new() {
                TickNum = _networkingClient.ClientTickBuffer.CurrTick,
                Position = tilePos,
                Foreground = foreground
            };

            _networkingClient.ClientTickBuffer.AddPacket(testBreak);
            if (_connect) {
                _networkingClient.SendBreakTile(testBreak);
            }
        }
    }

    public void Draw(GameTime gameTime, GraphicsDevice graphicsDevice) {
        graphicsDevice.Clear(Color.CornflowerBlue);

        _camera.Draw(graphicsDevice, _spriteBatch);

        const bool pixelPerfect = false;

        Rectangle destinationRectangle = pixelPerfect ? GetPixelPerfectRect() : GetFitRect();
        _camera.RenderLocation = destinationRectangle;

        graphicsDevice.Clear(Color.Black);

        _spriteBatch.Begin(samplerState: pixelPerfect ? SamplerState.PointClamp : null);
        _spriteBatch.Draw(_camera.RenderTarget, destinationRectangle, Color.White);
        
        float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

        _frameCounter.Update(deltaTime);

        _spriteBatch.End();
    }

    private Rectangle GetPixelPerfectRect() {
        int multiplier = int.Min(_game.GraphicsDevice.Viewport.Height / _camera.RenderTarget.Height,
            _game.GraphicsDevice.Viewport.Width / _camera.RenderTarget.Width);

        int width = _camera.RenderTarget.Width * multiplier;
        int height = _camera.RenderTarget.Height * multiplier;

        int x = (_game.GraphicsDevice.Viewport.Width - width) / 2;
        int y = (_game.GraphicsDevice.Viewport.Height - height) / 2;

        return new Rectangle(x, y, width, height);
    }

    private Rectangle GetFitRect() {
        float aspectRatio = (float)_game.GraphicsDevice.Viewport.Width / _game.GraphicsDevice.Viewport.Height;
        float renderTargetAspectRatio = (float)_camera.RenderTarget.Width / _camera.RenderTarget.Height;

        int width, height;
        if (aspectRatio > renderTargetAspectRatio) {
            width = (int)(_game.GraphicsDevice.Viewport.Height * renderTargetAspectRatio);
            height = _game.GraphicsDevice.Viewport.Height;
        } else {
            width = _game.GraphicsDevice.Viewport.Width;
            height = (int)(_game.GraphicsDevice.Viewport.Width / renderTargetAspectRatio);
        }

        int x = (_game.GraphicsDevice.Viewport.Width - width) / 2;
        int y = (_game.GraphicsDevice.Viewport.Height - height) / 2;

        return new Rectangle(x, y, width, height);
    }
    
    public void EndScene() {
        _networkingClient.Stop();
    }
}