using System.Threading.Tasks;
using MusicRpc.Models;
namespace MusicRpc.Players.Interfaces;
internal interface IMusicPlayer
{
    Task<PlayerInfo?> GetPlayerInfoAsync();
}